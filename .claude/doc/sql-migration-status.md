# Migration Azure SQL — terminée

> Le plan cible est dans [sql-migration-plan.md](sql-migration-plan.md).
> Ce fichier est le journal de ce qui a réellement été fait.
>
> Dernière mise à jour : **2026-08-02** (Macklin Softwarini)
> Branche : **`sql-migration`** (partie de `main` à `d9c8527`) — **non fusionnée**

---

## État

```
Phase 0  Filet de sécurité + infra    ████████████████████  fait
Phase 1  Projet Data (schéma)         ████████████████████  fait
Phase 2  Core dé-Firestorisé          ████████████████████  fait
Phase 3  Ingestion (6 jobs)           ████████████████████  fait
Phase 4  Écritures pool               ████████████████████  fait
Phase 5  Pointage                     ████████████████████  fait et validé
Phase 6  API                          ████████████████████  fait
Phase 7  Simulation                   ████████████████████  fait
Phase 8  Bascule                      ████████████████████  fait
+        CapWages (hors plan initial) ████████████████████  fait
```

**Firestore n'existe plus dans le code** : aucun paquet, aucune entité, aucun
job, aucun client. Ce qu'il en reste, ce sont des commentaires qui expliquent
pourquoi le modèle est ce qu'il est — la plupart de ce schéma existe pour
défaire des contraintes qui ne s'appliquent plus.

**Le frontend n'a pas été modifié d'une seule ligne.**

Tests : **151 verts** (124 Core, 27 Data). Net sur la branche : environ
+9 500 / −7 000 lignes.

---

## Pour mettre en ligne — ce qui reste, et c'est à toi

1. **Secrets GitHub** : `AZURE_SQL_CONNECTION`, `AZURE_CREDENTIALS` (service
   principal pour la règle de pare-feu des runners), variable
   `AZURE_RESOURCE_GROUP`.
2. **Pare-feu Azure** : ajouter l'IP de sortie de Cloud Run. La case « Autoriser
   les services Azure » ne la couvre pas.
3. **Fusionner `sql-migration` dans `main`**, puis Actions → « Deploy API to
   Cloud Run ».
4. **Vérifier le barème de Les Mordus** : la doc annonçait « victoire de gardien
   = 1 », le seed utilise **2** (ce que Firestore faisait réellement).

⚠️ Le `JoinCode` de la ligue change à chaque reseed :
`SELECT Name, JoinCode FROM Leagues;`

---

## Ce qui vit dans Azure SQL

| | |
|---|---|
| Serveur / base | `fantasywarrior.database.windows.net` / `fantasywarrior` |
| `Players` | 1 275 + 290 créés depuis les feuilles de match + 2 depuis le roster Mordus |
| `PlayerContracts` | 3 269 saisons-contrats **réelles** (CapWages) — 685 des 701 joueurs NHL actifs |
| `Games` / `PlayerGameStats` | 1 312 / 49 999 (saison 2025-26 complète) |
| `Periods` | 28 semaines, ancrées 2025-10-06 |
| Ligue | **Les Mordus**, 14 équipes, 360 roster spots |
| Curseur de simulation | **2025-10-04** (veille de la saison) |

État volontaire : les 14 équipes sont à **0**, les 360 lignes d'alignement
d'ouverture existent (183 actives) sans aucun point, les stats NHL sont
visibles. C'est la simulation qui donne vie aux assignments.

---

## La validation contre l'oracle — le résultat le plus important

En rejouant les semaines 1 et 2 et en comparant à `golden-scores-preSql.json`,
joueur par joueur :

- **Les alignements sont identiques.** Pour l'équipe testée, les deux systèmes
  choisissent exactement les mêmes 14 joueurs. Auto-remplissage, fenêtre de
  stats, banquage et vues reproduisent Firestore à l'identique.
- **Un seul écart par joueur, et c'est Firestore qui a tort.** Sergei
  Bobrovsky, semaine 1 : 3 matchs, **3 victoires** (FLA vs CHI, PHI, OTT). À
  2 pts la victoire, SQL calcule 6 ; Firestore avait `gamesPlayed=3, points=3`.
  Ses champs de décision de gardien n'étaient pas correctement captés.
- Cette seule classe d'erreur explique **tous** les écarts restants : ils vont
  tous dans le même sens (SQL ≥ Firestore), et les plus gros appartiennent aux
  équipes qui alignent le plus de départs de gardien.

**Conséquence** : l'écart de 1 265 lignes (49 999 vs 51 264) n'était pas une
perte côté SQL — l'ancien journal de match était partiellement faux. Le nouvel
import vient directement de l'API NHL.

⚠️ **Pour refaire cette comparaison**, semer avec `--no-opening-lineup` :
Firestore n'a jamais utilisé la liste `Active` du PDF, il auto-remplissait. Les
deux produisent des scores légitimes mais différents, et l'oracle ne valide le
moteur que si les *entrées* concordent.

La règle en cause est désormais épinglée par un test nommé d'après le cas :
`StatColumnsTests.ThreeGoalieWinsScoreSixUnderTheMordusScale`.

---

## Bugs silencieux trouvés en route

1. **Une semaine était banquée sans avoir été scorée.** L'étape 1 ne score que
   la semaine *en cours*, qui au moment où une semaine devient banquable est
   déjà la *suivante*. Les semaines étaient gelées sur des chiffres partiels —
   zéro, dans un rejeu. Le banquage rescore la semaine une dernière fois avant
   de la geler, ce qui donne aussi son sens au jour de grâce.
2. **`wipe-pools` laissait les périodes marquées « banquées ».** Les frontières
   de semaine sont du calendrier et survivent ; « banquée » est de l'état de
   pool et ne doit pas. La ligue fraîche ne pouvait plus jamais les banquer.
3. **L'auto-remplissage ne classait rien** — `SeasonPointsToDate` valait 0 pour
   tous, donc « les meilleurs disponibles » signifiait « les plus petits ids ».
4. **`season-stats` ignorait le curseur de simulation** alors que la carte
   joueur le respectait : le même joueur affichait 74 matchs d'un côté et 0 de
   l'autre, sur le même écran.
5. **Le `.gitignore` ne protégeait pas `appsettings.Local.json`** — le motif
   `appsettings.*.Local.json` exige un segment au milieu. Le dépôt est public.

---

## Ce que le schéma a supprimé

| Mécanisme Firestore | Devenu |
|---|---|
| 12 colonnes de score sur `Team` | 4 vues (`vStandings`, `vTeamPeriodScores`, `vRosterSpotTotals`, `vPlayerSeasonStats`) |
| `playerSeasonStats` + `throughDate` + invalidation | un `GROUP BY`, et un `WHERE` pour la version bornée |
| `SeasonStatsAdvance` | disparu — le problème n'existe plus |
| `finalizedThroughPeriodIndex` (garde anti-double-comptage) | disparu — banquer, c'est poser un drapeau |
| Union de 2 requêtes pour les spots d'une période | un `WHERE ... OR ...` |
| Scan applicatif « un propriétaire par joueur » | index unique filtré |
| `check-indexes`, compteurs de lectures, quota | disparus |
| Matérialisation des alignements de la semaine suivante | disparue — un alignement est un drapeau sur des lignes déjà créées |

Ce que la BD applique elle-même maintenant : un propriétaire par joueur par
ligue, un assignment par spot par semaine, un actif d'échange est un joueur
**ou** un pick, un vote « équitable » sans intensité, un seul curseur de
simulation.

---

## Conventions

- **Un seul grain honnête** : `RosterAssignment`. Tout total au-dessus est une
  vue. **Ne jamais rajouter de colonne de score sur `Teams`.**
- **Le frontend ne bouge pas.** Réponses JSON identiques au champ près.
  `league.id` = `JoinCode`, `spotId` = `RosterSpotId` en chaîne.
- **`PUT .../lineup`** écrit les drapeaux `IsActive` **et** une ligne
  `TeamPeriodLineups` au nom du GM. Le job lit cette attribution pour
  distinguer un vrai choix de son auto-remplissage.
- **Transactions** : toujours via `db.Database.CreateExecutionStrategy()`.
- **Migrations** : commande `db-migrate`, jamais au démarrage de l'API.

---

## Environnement

Credentials dans `backend/FantasyWarrior.{Jobs,Api}/appsettings.Local.json`
(hors dépôt). Voir [deployment.md](deployment.md) pour tout le reste, dont la
recette de reconstruction complète de la base.

---

## Reste ouvert (hors périmètre, modélisé au schéma)

Repêchage (mécanisme et écrans), agence libre, application réelle du plafond et
des tailles de roster, authentification, points du slot « Équipe », échanges à
trois. Les tables existent — les construire ne demandera pas de migration.

Aussi : **FantasySP renvoie 403** depuis le 2026-08-02, cette source est morte
jusqu'à ce que quelqu'un s'y penche. Les deux sources Rotowire fonctionnent.

---

## Commits (branche `sql-migration`)

| | |
|---|---|
| `fee4d8c` | Échanges à la frontière de période |
| `f76b990` | `dump-golden` — l'oracle de correction |
| `f761754` | Phase 1 : le schéma Azure SQL |
| `781848b` | `capwages-sync` — les vrais contrats |
| `f020104` | `player-sync` sur EF + chargement d'Azure SQL |
| `605c26e` | Plan + suivi rapatriés dans le dépôt |
| `394a404` | Credentials via appsettings |
| `ec1f7b2` | Ingestion + pointage sur SQL, saison ré-importée |
| `30e3f26` | Seed Les Mordus, alignement d'ouverture, rien de scoré |
| `06aa225` | `sim-advance` + preuve du moteur contre l'oracle |
| `9ce380a` | L'API sur SQL, DTO identiques au champ près |
| `50e0bfb` | Bascule : Firestore supprimé, 2 derniers jobs, préfixe retiré |
