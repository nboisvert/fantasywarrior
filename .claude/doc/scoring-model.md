# Modèle de pointage — référence

> La référence pour toute règle de pointage. Si le code et ce document divergent, l'un des deux est un bug.
> Dernière mise à jour : 2026-08-25 (protection d'entre-saison, §11).

## En une phrase

Chaque semaine, un GM active un sous-ensemble de son roster. Seuls les points des joueurs **actifs** comptent, et **ils sont acquis définitivement**.

---

## 1. Les entités

| Entité | Table | Ce que c'est |
|---|---|---|
| **Period** | table `Periods` (globale) | Une semaine de pointage. Lundi→dimanche sur la date de match NHL (Est). ~28 par saison. |
| **RosterSpot** | table `RosterSpots` | L'appartenance d'un **joueur ou d'une franchise** à une équipe, du jour X au jour Y. Jamais supprimé, seulement fermé. Porte aussi `ProtectionStatus`, hors pointage — voir §11. |
| **RosterAssignment** | table `RosterAssignments` | Ce qu'un spot a produit une semaine, et s'il était actif. **Une ligne par (spot, semaine)** — le seul grain stocké. |
| **Team** | table `Teams` | Ne porte **aucun cumul**. Tous les totaux sont des vues (`vStandings`, `vTeamPeriodScores`, `vRosterSpotTotals`). |

**Invariant central** : score = finalizedScore + livePoints. Il tient désormais **par construction** — les deux côtés sortent de la même requête sur les mêmes lignes, au lieu d'être trois champs tenus à la main.

### Le slot Équipe (`T`)

Depuis le 2026-08-05, un `RosterSpot` tient **un joueur ou une franchise NHL** — groupe de position `T`, une seule par équipe, garantie par index unique filtré. C'est un spot ordinaire à tous les égards : il produit une ligne `RosterAssignment` par semaine, ses points se banquent, il s'échange. Trois différences, toutes conséquences de « une seule par équipe » :

- **Jamais au banc.** Une franchise, un siège, donc aucune décision à prendre : pas de bouton actif/banc, et `LineupRules.LegalActiveSet` la garde active quoi qu'on lui soumette.
- **Ses statistiques viennent de `Games`**, pas du journal de match des joueurs. `FranchiseResults.For` est la seule chose que le slot ne partage pas avec un joueur.
- **Ne coûte rien au plafond et n'est pas un joueur** : `vStandings` l'exclut de `CapTotal`, `PlayerCount` et `UnknownContracts`, et un échange ne la déplace que **contre une autre franchise**.

Elle ne rapporte **aucun `gamesPlayed`** : le slot annonce une fiche, pas une charge de travail. Effet de bord voulu — `RosterGamesPlayed`, dénominateur de tous les points-par-match de l'app, reste une affaire de joueurs.

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
| Chaque nuit | Les alignements de la semaine **suivante** sont créés par report, s'ils n'existent pas déjà. Jamais réécrits. |
| **Fin de dimanche + 1 jour de grâce** | La semaine est **banquée** : ses points rejoignent `finalizedScore` et n'en bougeront plus jamais. |

Le **jour de grâce** existe parce que la NHL corrige ses feuilles de match après coup. Banquer le soir même figerait ce qu'on savait cette nuit-là, et une correction du lendemain serait perdue en silence.

### Les trades ne sont plus dans ce cycle

**Accepter un échange l'exécute** (`TradeExecution`), daté au **lundi suivant** : le spot sortant reçoit une `EndDate` au dimanche, le spot entrant une `StartDate` au lundi, et les alignements de cette semaine-là suivent — le partant perd le sien, l'arrivant en reçoit un, inactif.

L'effet n'a pas bougé d'un jour : un échange prend toujours effet à une frontière de semaine, et un joueur n'a jamais deux propriétaires le même jour. Ce qui a changé, c'est **quand la base est mise au courant**.

C'est ce qui rend l'écran d'alignement honnête. Le picker règle la semaine suivante ; tant que les rosters ne bougeaient qu'au job de nuit, il proposait un joueur qui serait parti et cachait celui qui allait arriver.

> **Avant le 2026-08-07**, le job nocturne exécutait les échanges la nuit où une semaine était banquée, effectifs « à la première semaine commençant après la dernière journée de stats ». Le jour de grâce faisait tomber ça une semaine plus tard que ce que ce document annonçait. La date était une conséquence de l'ordonnancement, pas une règle.

---

## 4. Les règles

### Verrouillage
Le lineup de la semaine N doit être soumis **avant** que la semaine N commence. C'est la seule option à l'épreuve de la triche sans passer à des alignements quotidiens : sans elle, un GM pourrait activer un joueur après qu'il ait compté quatre buts le lundi.

**Conséquence à assumer** : un joueur acquis en cours de semaine est au banc jusqu'au lundi suivant.

### Lineup oublié
Le lineup de la semaine précédente est **reporté automatiquement**, moins les joueurs qui ont quitté le roster, puis **complété** par les meilleurs disponibles à chaque position. Un GM en vacances n'est pas puni — dans un pool entre amis, ça viderait le classement de son sens.

La rangée porte `setBy: "auto"` pour que l'interface puisse le signaler.

Le report est **écrit** par `WeekAheadJob`, chaque nuit, et ne réécrit jamais une rangée existante — c'est toute la règle. L'endpoint calculait autrefois un aperçu de ce que le report *aurait* choisi, sans jamais l'écrire ; les rangées n'apparaissaient donc qu'au passage du pointage, quand la semaine était déjà verrouillée.

### Slots
Configurables par le commissaire, par position. **Les Mordus : 9 attaquants, 4 défenseurs, 1 gardien.**

C'est la **seule règle réellement appliquée** dans l'app. La taille de roster et le plafond salarial sont affichés mais jamais validés.

Aligner **moins** que le maximum est permis — on marque simplement moins. Aligner **plus** est refusé.

### Transactions
**Tout prend effet au rollover de période**, trades comme agents libres. Un roster spot ne commence donc jamais en milieu de semaine, ce qui élimine toute une catégorie de cas de bord.

**Un roster spot peut commencer dans le futur.** C'est la propriété dont tout le reste découle depuis le 2026-08-07 : « jamais fermé » et « détenu aujourd'hui » ont cessé d'être la même question, et elles se séparent pour exactement les deux spots qu'un échange crée.

| Question | Prédicat (`RosterWindow`) | Sert à |
|---|---|---|
| Détenu aujourd'hui | `Start <= today && (End == null \|\| End >= today)` | le roster affiché, le **plafond** |
| Détenu cette semaine-là | `Start <= fin && (End == null \|\| End >= début)` | l'alignement, le pointage |
| **Engagé** | `End == null` | ce qu'on valide un échange contre |
| *engaged* (la mention) | détenu aujourd'hui **et** `End != null` | « part bientôt » à l'écran |

L'ancien filtre `EndDate IS NULL` n'est pas devenu faux : il a changé de sens. Il désigne maintenant l'engagé.

### Points acquis
Une fois une semaine banquée, ses points appartiennent définitivement à l'équipe qui a aligné le joueur. **Un échange ne peut pas déplacer l'historique.**

C'est ce qui a permis de supprimer tout le système de compensation (`Adjustment`) : il n'y a plus rien à compenser.

**Corollaire** : changer le barème en cours de saison ne recalcule pas le passé. Le total devient un mélange de deux barèmes — défendable, mais à assumer. La porte de sortie est de dé-banquer puis rejouer (§10) — **pas** une commande `recompute` : elle est citée ici, dans un commentaire de `RosterAssignment.cs` et dans le message d'erreur de `PATCH /api/leagues/{code}/rules`, mais n'existe pas dans `Jobs/Program.cs`. Un job du même nom que `sim-reset` ou `set-league-rules` avant lui.

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

**Les Mordus** : but 1, passe 1, **victoire de gardien 2**, défaite en prolongation 1, blanchissage 0. Slot Équipe : **victoire 2, défaite en prolongation 1**, défaite 0.

Les trois clés d'équipe (`teamWins`, `teamLosses`, `teamOtLosses`) sont **distinctes** de celles du gardien. Elles valent la même chose ici, et c'est une coïncidence : « mon gardien a gagné » et « ma franchise a gagné » sont deux événements différents le même soir, et une ligue qui veut les payer différemment n'a aucun moyen de le dire s'ils partagent une clé. Elles passent par `extraPointValues` comme n'importe quelle stat — aucun code spécial, c'est précisément le mécanisme décrit ci-dessus.

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
| Coût d'un joueur sans contrat | `ruleConfig.defaultCapHit` | **oui**, dans `vStandings` (colonnes du jour *et* engagées) et la validation d'échange |
| Valeur du slot Équipe | `ruleConfig.extraPointValues` (`teamWins`/`teamLosses`/`teamOtLosses`) | oui, au calcul |
| Choix au repêchage par équipe par année | `ruleConfig.draftRounds` | un par ronde, généré par `draft-picks-init` |

**Un joueur sans contrat coûte 1 M$ par défaut** (Nick, 2026-08-05), réglable par
ligue — 0 rétablit l'ancien comportement. « Sans contrat » n'est pas un trou de
données à combler : c'est l'état permanent et ordinaire d'un autonome non signé
et d'un espoir repêché qui n'a pas encore signé, et un pool keeper en compte
beaucoup. Les compter à 0 $ laissait un DG en accumuler gratuitement et
sous-estimait chaque total.

`vStandings` expose aussi `UnknownContracts` — une fois le salaire supposé fondu
dans le total, plus rien ne le distingue d'un vrai, et un chiffre mi-mesuré
mi-conventionnel doit le dire.

**Le plafond est appliqué contre les chiffres *engagés*, pas contre le
classement** (2026-08-03). Un échange accepté est irréversible. Valider contre
le plafond du jour laisserait un GM accepter un contrat de 9 M$ le matin et
faire sauter le plafond l'après-midi, chaque échange paraissant légal isolément.

Depuis le 2026-08-07 les deux chiffres sortent de la **même vue** :
`CapTotal`/`PlayerCount` filtrent les spots détenus aujourd'hui,
`EngagedCapTotal`/`EngagedPlayerCount` ceux qui restent une fois tous les
échanges arrivés. Même agrégation, un filtre d'écart, un seul énoncé — ils ne
peuvent plus diverger, alors qu'auparavant `vTeamCommitments` portait un delta
que l'appelant additionnait.

Même logique pour les actifs : un joueur ou un choix déjà en mouvement dans un
échange accepté ne peut pas être réoffert. Les offres *en attente*, elles, ne
verrouillent rien.

Ce qui n'est toujours **pas** appliqué : rien n'empêche un roster d'être hors
limites par un autre chemin — il n'existe simplement aucun autre chemin
aujourd'hui (pas d'ajout/retrait de joueur libre).

Tout cela se règle par l'API — `PATCH /api/leagues/{code}/rules`, réservé au
commissaire — et par le panneau **League rules** de l'app.

> Il n'existe **pas** de job `set-league-rules`. Cette ligne en décrivait un
> jusqu'au 2026-08-05; il n'a jamais existé sous Azure SQL. Le plafond lui-même
> (`capAmount`) n'est d'ailleurs pas encore éditable par ce PATCH : il se fixe à
> la création de la ligue ou par `seed-mordus --cap`.

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
- ~~Le slot **Équipe** ne rapporte aucun point~~ — **fait le 2026-08-05**. C'est un `RosterSpot` de groupe `T` (§1), il vaut 2 par victoire et 1 par défaite en prolongation chez Les Mordus, et il s'échange contre une autre franchise. La saison a été rejouée depuis la semaine 1 pour que le classement soit homogène.
- Les salaires sont **réels** depuis 2026-08-02 (CapWages, table `PlayerContracts`).
  Ceux qui n'en ont pas comptent **1 M$** depuis 2026-08-05 (§9), et non plus 0 $.
  Leur nombre voyage avec le total (`UnknownContracts`) : on ne peut toujours pas
  valider un salaire que personne n'a au dossier, on refuse simplement de faire
  comme s'il était nul. Chez Les Mordus, 30 joueurs sont dans ce cas.
- Le repêchage lui-même n'existe pas. Les choix se créent et s'échangent ; rien
  ne les convertit encore en joueurs (`DraftPick.UsedUtc` n'est jamais écrit).

### La protection d'entre-saison — la fondation seulement (2026-08-25)

**Hors pointage entièrement.** Une protection ne change ni un alignement, ni des
points, ni un plafond ; elle est ici parce qu'elle vit sur un `RosterSpot`, et que
ce document doit être lu avant de toucher aux spots.

La règle de la ligue : entre deux saisons, chaque DG protège un nombre limité de
joueurs ; les autres sont exposés et peuvent être **volés** pendant les deux
premières rondes du repêchage. Les exposés que personne ne réclame restent chez
eux — ils n'ont jamais bougé.

Ce qui existe aujourd'hui :

| | |
|---|---|
| `RosterSpots.ProtectionStatus` | La décision du DG. `Unprotected` pour tout le monde ; **rien ne peut encore écrire `Protected`.** |
| `Players.CareerNhlGames` | Matchs LNH en carrière, écrit par `career-sync`. |
| `ProtectionRules.IsAutoProtected` | Le verdict : gardien ≤ 50 matchs, patineur ≤ 100 → intouchable, **gratuitement** (ça ne consomme aucune place). |
| `protection-reset --league` | Efface l'ardoise. Une protection ne vaut qu'un été. |
| La pastille `AUTO` | Sur la carte joueur, et seulement là. |

**On stocke la mesure, on dérive le verdict.** Le nombre de matchs est une donnée
de référence à un seul écrivain ; l'auto-protection est une comparaison à un
seuil. Les séparer permet de déplacer un seuil sans réécrire une ligne, et garde
la règle écrite à un seul endroit.

**Zéro n'est pas « on ne sait pas. »** `CareerNhlGames` est null exactement quand
`CareerStatsSyncedUtc` l'est, `autoProtected` est alors null, et l'interface
n'affiche rien — plutôt que de coller une pastille `AUTO` sur un vétéran dont la
synchro a échoué.

Ce qui reste devant, quand le repêchage arrivera : geler le calcul le jour même
(un joueur qui dispute son 100e match entre deux rondes changerait de catégorie),
ajouter `RosterSpotEndReason.Draft`, geler les échanges pendant la fenêtre (un
échange ouvre un spot neuf, qui n'hérite d'aucune protection), et exclure le slot
`T` des volables. Le vol lui-même est déjà écrit : c'est
`RosterChange.ApplyAsync` avec `startReason: Draft`.
