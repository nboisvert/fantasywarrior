# Modèle de pointage — référence

> La référence pour toute règle de pointage. Si le code et ce document divergent, l'un des deux est un bug.
> Dernière mise à jour : 2026-08-02 (migration Azure SQL).

## En une phrase

Chaque semaine, un GM active un sous-ensemble de son roster. Seuls les points des joueurs **actifs** comptent, et **ils sont acquis définitivement**.

---

## 1. Les entités

| Entité | Table | Ce que c'est |
|---|---|---|
| **Period** | table `Periods` (globale) | Une semaine de pointage. Lundi→dimanche sur la date de match NHL (Est). ~28 par saison. |
| **RosterSpot** | table `RosterSpots` | L'appartenance d'un joueur à une équipe, du jour X au jour Y. Jamais supprimé, seulement fermé. |
| **RosterAssignment** | table `RosterAssignments` | Ce qu'un spot a produit une semaine, et s'il était actif. **Une ligne par (spot, semaine)** — le seul grain stocké. |
| **Team** | table `Teams` | Ne porte **aucun cumul**. Tous les totaux sont des vues (`vStandings`, `vTeamPeriodScores`, `vRosterSpotTotals`). |

**Invariant central** : score = finalizedScore + livePoints. Il tient désormais **par construction** — les deux côtés sortent de la même requête sur les mêmes lignes, au lieu d'être trois champs tenus à la main.

---

## 2. Les trois niveaux d'agrégation

```
lineup.results[spotId].points        ce qu'un joueur a produit cette semaine
        ↓ somme des actifs
lineup.activePoints                  ce que l'équipe a marqué cette semaine
        ↓ banqué à la fin de la semaine
team.finalizedScore                  cumul des semaines terminées, immuable
        ↓ + la semaine en cours
team.score                           ce qui s'affiche au classement
```

En parallèle, `rosterSpot.activePoints` cumule ce qu'un joueur a rapporté à **cette** équipe — c'est ce qui alimente la colonne PTS de l'écran Team.

---

## 3. Le cycle hebdomadaire

| Moment | Ce qui arrive |
|---|---|
| **Lundi 00h00 ET** | La semaine démarre et **le lineup se verrouille**. Plus aucune modification. |
| Lundi → dimanche | Le job nocturne recalcule la semaine en cours **à zéro** chaque nuit. Rien ne s'accumule. |
| **Fin de dimanche + 1 jour de grâce** | La semaine est **banquée** : ses points rejoignent `finalizedScore` et n'en bougeront plus jamais. |
| Au même rollover | Les trades acceptés s'exécutent, effectifs au début de la semaine suivante. |
| Puis | Les lineups de la semaine suivante sont créés par report. |

Le **jour de grâce** existe parce que la NHL corrige ses feuilles de match après coup. Banquer le soir même figerait ce qu'on savait cette nuit-là, et une correction du lendemain serait perdue en silence.

---

## 4. Les règles

### Verrouillage
Le lineup de la semaine N doit être soumis **avant** que la semaine N commence. C'est la seule option à l'épreuve de la triche sans passer à des alignements quotidiens : sans elle, un GM pourrait activer un joueur après qu'il ait compté quatre buts le lundi.

**Conséquence à assumer** : un joueur acquis en cours de semaine est au banc jusqu'au lundi suivant.

### Lineup oublié
Le lineup de la semaine précédente est **reporté automatiquement**, moins les joueurs qui ont quitté le roster, puis **complété** par les meilleurs disponibles à chaque position. Un GM en vacances n'est pas puni — dans un pool entre amis, ça viderait le classement de son sens.

Le document porte `setBy: "auto"` pour que l'interface puisse le signaler.

### Slots
Configurables par le commissaire, par position. **Les Mordus : 9 attaquants, 4 défenseurs, 1 gardien.**

C'est la **seule règle réellement appliquée** dans l'app. La taille de roster et le plafond salarial sont affichés mais jamais validés.

Aligner **moins** que le maximum est permis — on marque simplement moins. Aligner **plus** est refusé.

### Transactions
**Tout prend effet au rollover de période**, trades comme agents libres. Un roster spot ne commence donc jamais en milieu de semaine, ce qui élimine toute une catégorie de cas de bord.

### Points acquis
Une fois une semaine banquée, ses points appartiennent définitivement à l'équipe qui a aligné le joueur. **Un échange ne peut pas déplacer l'historique.**

C'est ce qui a permis de supprimer tout le système de compensation (`Adjustment`) : il n'y a plus rien à compenser.

**Corollaire** : changer le barème en cours de saison ne recalcule pas le passé. Le total devient un mélange de deux barèmes — défendable, mais à assumer. La commande `recompute` est la porte de sortie.

### Séries éliminatoires
**Exclues.** Le filtre `gameType == 2` s'applique partout. C'est une règle, pas un accident.

### Semaines mortes
Une semaine sans match (pause olympique, match des étoiles) existe quand même et rapporte zéro. Le champ `gameCount` permet à l'interface d'afficher « pause » plutôt qu'un 0 inexpliqué. **La saison 2025-26 en compte deux** (9 au 22 février 2026, Milan-Cortina).

---

## 5. La formule

```
points d'un joueur pour une semaine = Σ (stat × valeur du barème)
```

Le barème est une **map clé→valeur** sur des noms de stats (`StatKeys`), pas une liste fixe. Un commissaire peut donc scorer les tirs bloqués, les mises en échec ou même les matchs joués **sans changement de schéma**.

**Les Mordus** : but 1, passe 1, **victoire de gardien 2**, défaite en prolongation 1, blanchissage 0.

Les cinq valeurs historiques vivent dans `pointValues`; toute autre stat va dans `extraPointValues`. `RuleConfig.ScoringScale()` fusionne les deux — c'est la seule forme que le moteur consomme.

Une clé inconnue est **rejetée par l'API**, pas absorbée : elle marquerait zéro pour toujours et ressemblerait à un bug de calcul plutôt qu'à une faute de frappe.

---

## 6. Fenêtre de calcul

Trois choses restreignent ce qu'un roster spot possède d'une semaine, et les trois comptent :

1. **Le spot peut avoir ouvert ou fermé en cours de semaine** — un joueur échangé le jeudi garde ses points de lundi à mercredi pour son ancienne équipe.
2. **`lastStatDate` borne la fin** — scorer un jour dont les feuilles de match ne sont pas encore synchronisées y banquerait un zéro sans jamais y revenir.
3. **Un spot ouvert après le dernier jour synchronisé ne possède rien** — `null`, pas une plage vide.

C'est `StatWindow.Intersect`, la fonction la plus critique du modèle.

---

## 7. Pourquoi le calendrier est global

Une semaine est une propriété du calendrier NHL, pas du pool. Le partager entre toutes les ligues permet au job nocturne de récupérer les lignes de match de la semaine en **une seule requête par plage de dates**, servant toutes les ligues à la fois.

Coût mesuré : **~1 600 lectures par nuit** contre ~90 000 pour l'ancien modèle, et ça ne croît pas avec la saison puisque les semaines terminées ne sont jamais relues.

Des calendriers par ligue ramèneraient une requête par ligue — c'est la raison technique pour laquelle ce choix n'est pas négociable.

---

## 8. Propriétés à ne pas casser

- **Idempotence.** La semaine en cours est recalculée à zéro, jamais accumulée. Banquer est protégé par `finalizedThroughPeriodIndex`, écrit dans la **même** mise à jour atomique que la valeur qu'il garde. Relancer le job nocturne est sans effet.
- **Provenance de l'alignement.** Le GM écrit `RosterAssignment.IsActive` et une ligne `TeamPeriodLineups` à son nom ; le job écrit les stats et les points. Le job lit cette attribution pour distinguer un vrai choix de son propre auto-remplissage — sans elle, il écraserait les décisions du GM.
- **Périodes immuables.** Déplacer une frontière après coup restaterait des points acquis. `period-init` n'ajoute jamais, ne réécrit jamais.
- **Soumission transactionnelle.** L'alignement complet est validé puis écrit dans une transaction, donc deux onglets ne peuvent pas produire un roster illégal.

---

## 9. Paramètres du commissaire

| Paramètre | Où | Appliqué ? |
|---|---|---|
| Valeurs de points (5 fixes + extras) | `ruleConfig.pointValues` / `.extraPointValues` | oui, au calcul |
| Slots actifs par position | `ruleConfig.topCount` | **oui, à la soumission du lineup** |
| Taille de roster min/max | `ruleConfig.rosterSize` | **oui, sur les échanges** (proposition et acceptation) |
| Plafond salarial | `league.capAmount` | **oui, sur les échanges** (proposition et acceptation) |
| Choix au repêchage par équipe par année | `ruleConfig.draftRounds` | un par ronde, généré par `draft-picks-init` |

**Le plafond est appliqué contre les chiffres *engagés*, pas contre le
classement** (2026-08-03). Un échange accepté est irréversible : il s'exécute à
la frontière de semaine, mais `vStandings` décrit encore le roster
d'aujourd'hui. Valider contre lui laisserait un GM accepter un contrat de 9 M$
le matin et faire sauter le plafond l'après-midi, chaque échange paraissant
légal isolément. `vTeamCommitments` porte la différence. Même logique pour les
actifs : un joueur ou un choix déjà en mouvement dans un échange accepté ne peut
pas être réoffert. Les offres *en attente*, elles, ne verrouillent rien.

Ce qui n'est toujours **pas** appliqué : rien n'empêche un roster d'être hors
limites par un autre chemin — il n'existe simplement aucun autre chemin
aujourd'hui (pas d'ajout/retrait de joueur libre).

En ligne de commande : `set-league-rules --league <id> [--goal N] [--assist N] [--goalie-win N] [--goalie-otl N] [--shutout N] [--forwards N] [--defense N] [--goalies N] [--roster-min N] [--roster-max N] [--cap N]`.

---

## 10. Opérations

| Besoin | Commande |
|---|---|
| Générer le calendrier d'une saison | `period-init --season 20262027` |
| Générer les choix au repêchage | `draft-picks-init --league <joinCode> [--year YYYY]` |
| Tourner le pointage (nocturne) | `nightly` |
| Rattraper un cron manqué / une saison importée | `nightly --backfill-from N` |
| Dé-banquer pour recalculer | `UPDATE RosterAssignments SET IsFinalized = 0` + `Periods.FinalizedUtc = NULL`, puis `nightly --backfill-from N` |
| Comparer à l'ancien modèle | `.claude/doc/golden-scores-preSql.json` (méthode et résultat dans [data-model.md](data-model.md)) |
| Déplacer un verrou de semaine | `UPDATE Periods SET LockUtc = ... WHERE Season = ... AND Number = ...` |

Un backfill de saison complète est redevenu une opération ordinaire depuis le passage à Azure SQL — il n'y a plus de quota de lectures à ménager.

---

## 11. Ce qui n'est pas fait

- **Aucune authentification.** L'API fait confiance au `username` envoyé. Avec les lineups c'est nettement plus grave qu'avant : on peut discrètement mettre le meilleur joueur d'un rival au banc chaque dimanche soir, et ça ressemble à son propre oubli. **À régler avant que de vrais utilisateurs y touchent.**
- Le slot **Équipe** (`team.franchiseAbbrev`) porte l'identité mais ne rapporte encore aucun point — règle à obtenir de Nick.
- Les salaires sont **réels** depuis 2026-08-02 (CapWages, table `PlayerContracts`) — 685 des 701 joueurs NHL actifs.
  Les **16 restants comptent 0 $**, dans `vStandings` comme dans la validation
  d'échange. C'est signalé à l'écran plutôt qu'avalé : on ne peut pas valider un
  salaire que personne n'a au dossier.
- Le repêchage lui-même n'existe pas. Les choix se créent et s'échangent ; rien
  ne les convertit encore en joueurs (`DraftPick.UsedUtc` n'est jamais écrit).
