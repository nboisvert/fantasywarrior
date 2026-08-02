# Migration Azure SQL — suivi

> **Le fichier à lire pour reprendre le chantier.** Le plan cible est dans
> [sql-migration-plan.md](sql-migration-plan.md) et ne bouge pas ; celui-ci
> suit l'avancement réel et doit être mis à jour à chaque session.
>
> Dernière mise à jour : **2026-08-02** (Macklin Softwarini)
> Branche : **`sql-migration`** (partie de `main` à `d9c8527`)

---

## Où on en est

```
Phase 0  Filet de sécurité + infra    ████████████████████  fait
Phase 1  Projet Data (schéma)         ████████████████████  fait
Phase 2  Core dé-Firestorisé          ████░░░░░░░░░░░░░░░░  partiel (DateOnly fait)
Phase 3  Ingestion                    ████████████████░░░░  4 jobs sur 6
Phase 4  Écritures pool               ████████████████░░░░  jobs faits, API non
Phase 5  Pointage                     ████████████████████  fait et validé
Phase 6  API                          ░░░░░░░░░░░░░░░░░░░░  pas commencé  ← LA SUITE
Phase 7  Simulation                   ██████████████░░░░░░  sim-advance/clock faits
Phase 8  Bascule                      ░░░░░░░░░░░░░░░░░░░░  pas commencé
+        CapWages (hors plan initial) ████████████████████  fait
```

**Rien n'est déployé.** La prod tourne toujours sur Cloud Run + Firestore,
inchangée. Le frontend n'a **pas été modifié d'une ligne** et n'a pas encore été
branché sur la nouvelle base — c'est la phase 6.

**Le moteur de pointage est prouvé** contre l'oracle Firestore (voir plus bas).

---

## Ce qui vit dans Azure SQL en ce moment

| | |
|---|---|
| Serveur / base | `fantasywarrior.database.windows.net` / `fantasywarrior` |
| Migrations | 3 appliquées |
| `Players` | 1 275 + 290 créés depuis les feuilles de match + 2 depuis le roster Mordus |
| `PlayerContracts` | 3 269 saisons-contrats réelles (CapWages) |
| `Games` / `PlayerGameStats` | **1 312** / **49 999** (saison 2025-26 complète) |
| `Periods` | 28 semaines, ancrées 2025-10-06 |
| Ligue | **Les Mordus**, 14 équipes, 360 roster spots |
| Curseur de simulation | **2025-10-04** (veille de la saison) |

**État volontaire, conforme à l'attente de Nick** : les 14 équipes sont à **0**,
les 360 lignes d'alignement d'ouverture existent (183 actives) mais ne portent
**aucun point**, et les stats NHL des joueurs sont visibles. C'est la simulation
qui donne vie aux assignments.

⚠️ Le `JoinCode` change à chaque reseed. Le lire avant de tester l'API :
`SELECT Name, JoinCode FROM Leagues;`

---

## La validation contre l'oracle — le résultat le plus important

En rejouant les semaines 1 et 2 puis en comparant à
`golden-scores-preSql.json`, joueur par joueur :

- **Les alignements sont identiques.** Pour l'équipe testée, les deux systèmes
  choisissent exactement les mêmes 14 joueurs. L'auto-remplissage, la fenêtre de
  stats, le banquage et les vues reproduisent Firestore à l'identique.
- **Un seul écart par joueur, et c'est Firestore qui a tort.** Sergei
  Bobrovsky, semaine 1 : 3 matchs, **3 victoires** (FLA vs CHI, PHI, OTT). À
  2 pts la victoire, SQL calcule 6. Firestore avait `gamesPlayed=3, points=3` —
  ses champs de décision de gardien n'étaient pas correctement captés.
- Cette seule classe d'erreur explique **tous** les écarts restants : ils vont
  tous dans le même sens (SQL ≥ Firestore) et les plus gros appartiennent aux
  équipes qui alignent le plus de départs de gardien.

**Conséquence** : l'écart de 1 265 lignes (49 999 vs 51 264) n'est pas une
perte de données côté SQL — l'ancien journal de match était partiellement faux.
Le nouvel import vient directement de l'API NHL.

⚠️ **Pour refaire cette comparaison**, il faut semer avec
`--no-opening-lineup` : Firestore n'a jamais utilisé la liste `Active` du PDF,
il auto-remplissait. Les deux produisent des scores légitimes mais différents,
et l'oracle ne valide le moteur que si les *entrées* concordent.

---

## Trois bugs silencieux trouvés grâce à l'oracle

1. **Une semaine était banquée sans avoir été scorée.** L'étape 1 ne score que
   la semaine *en cours*, qui au moment où une semaine devient banquable est
   déjà la *suivante*. Les semaines étaient donc gelées sur leurs chiffres
   partiels — zéro, dans un rejeu. Le banquage rescore maintenant la semaine
   une dernière fois avant de la geler, ce qui est aussi ce qui donne son sens
   au jour de grâce.
2. **`wipe-pools` laissait toutes les périodes marquées « banquées ».** Les
   frontières de semaine sont du calendrier et survivent ; « cette semaine est
   banquée » est de l'état de pool et ne doit pas survivre. La ligue fraîche ne
   pouvait plus jamais banquer ces semaines.
3. **L'auto-remplissage ne classait rien.** `SeasonPointsToDate` valait 0 pour
   tous les candidats, donc « les meilleurs joueurs disponibles » signifiait
   « ceux dont l'id est le plus petit ».

---

## Détail par phase

### ✅ Phase 0 — `dump-golden` + `golden-scores-preSql.json` (624 Ko, versionné)

### ✅ Phase 1 — `FantasyWarrior.Data` : 20 entités, 4 vues, 3 migrations, 19 tests

### 🔶 Phase 2 — Core dé-Firestorisé (partiel)

Fait : `StatWindow.Intersect` et `PeriodScoring.ShouldFinalize` passent en
`DateOnly`, avec une surcharge `string` qui parse et délègue — **une seule
implémentation de chaque règle** pendant la cohabitation.

Reste : supprimer les entités `[FirestoreData]` de Core, `PlayerTotalsSource`,
`SeasonStatsAdvance`, `RosterSpots`, `RosterChange` (version Firestore), et la
référence au paquet `Google.Cloud.Firestore`. **À faire en phase 8**, en même
temps que la suppression des jobs Firestore — c'est ce qui évite un long build
cassé.

⚠️ `Core.Tests` référence `Jobs`, donc vider Core casse la compilation des
tests tant que les jobs Firestore existent. Les supprimer ensemble.

### 🔶 Phase 3 — Ingestion (4 sur 6)

| Job | État |
|---|---|
| `sql-player-sync` | ✅ 1 275 joueurs |
| `sql-stats-sync` | ✅ 1 312 matchs / 49 999 lignes |
| `sql-period-init` | ✅ 28 semaines |
| `capwages-sync` | ✅ 3 269 contrats |
| `draft-sync` | ⬜ à porter |
| `news-sync` | ⬜ à porter |

### ✅ Phase 4 — Écritures pool (côté jobs)

`sql-seed-mordus`, `sql-wipe-pools`, `RosterChange` (une transaction),
`sql-process-trades`. **Les endpoints API correspondants sont la phase 6.**

### ✅ Phase 5 — Pointage

`sql-period-rollup` écrit **une ligne `RosterAssignment` par (spot, semaine) et
rien d'autre**. `sql-nightly` garde l'ordre : scorer → banquer → échanges.
Validé contre l'oracle.

### ⬜ Phase 6 — API ← **la suite**

Rien de commencé. `backend/FantasyWarrior.Api/Program.cs` (889 lignes) est
encore intégralement sur Firestore. À faire :

- Injecter `FantasyWarriorDbContext` (`AddFantasyWarriorData()`) au lieu de
  `FirestoreDb`, supprimer `PlayerCache`.
- **Réponses JSON identiques au champ près** — le contrat exact est dans
  `frontend/src/api.ts`. `league.id` → `JoinCode`, `spotId` →
  `RosterSpotId.ToString()`, routes toujours indexées par username.
- Les endpoints de lecture deviennent des requêtes sur les vues :
  `vStandings` pour `teams[]`, `vRosterSpotTotals` pour les colonnes `spot*` de
  `season-stats`, `vPlayerSeasonStats` pour la carte joueur.
- `PUT .../lineup` doit **créer/mettre à jour les lignes `RosterAssignment`**
  (`IsActive`) + une ligne `TeamPeriodLineup` avec `SetBy = username`. C'est ce
  que le rollup lit pour distinguer un choix de GM d'un auto-remplissage.

### 🔶 Phase 7 — Simulation

`sql-sim-clock` et `sql-sim-advance` faits et validés. Reste `sim-reset`
(aujourd'hui remplacé par `sql-wipe-pools` + reseed, ce qui marche mais perd les
rosters).

### ⬜ Phase 8 — Bascule

Supprimer tout le code Firestore (Core + Jobs + Api), les workflows, mettre à
jour `CLAUDE.md`, `deployment.md`, `project_status.md`, `scoring-model.md`,
`testmode.md`.

---

## Conventions

- **Préfixe `sql-`** : un job porté cohabite avec son ancêtre Firestore sous
  `Jobs/Sql/`. La phase 8 supprime les anciens et laisse tomber le préfixe.
- **Le frontend ne bouge pas.** Réponses JSON identiques au champ près.
- **Un seul grain honnête** : `RosterAssignment`. Ne jamais rajouter de colonne
  de score sur `Teams`.
- **Transactions** : toujours via `db.Database.CreateExecutionStrategy()` —
  `EnableRetryOnFailure` interdit les transactions manuelles, et sur le tier
  serverless un retry doit rejouer la transaction entière.

---

## Environnement local

Les credentials sont dans `backend/FantasyWarrior.{Jobs,Api}/appsettings.Local.json`
(**hors dépôt** — le dépôt est public). Plus besoin de variable d'environnement.

```powershell
cd C:\Nick\fw
dotnet run --project backend/FantasyWarrior.Jobs -- db-migrate --list
dotnet run --project backend/FantasyWarrior.Jobs -- sql-wipe-pools
dotnet run --project backend/FantasyWarrior.Jobs -- sql-seed-mordus [--no-opening-lineup]
dotnet run --project backend/FantasyWarrior.Jobs -- sql-sim-clock --set 2025-10-04 --season 20252026
dotnet run --project backend/FantasyWarrior.Jobs -- sql-sim-advance --to 2025-11-23
dotnet test FantasyWarrior.slnx        # 200 Core + 19 Data
```

Reconstruire la saison de zéro (~10 min) :
`sql-stats-sync --from 2025-10-07 --to 2026-04-16` puis `sql-period-init`.

- `dotnet-ef` 10.0.10 en outil global. Tests d'intégration sur **LocalDB**.
- API NHL et capwages.com **joignables** depuis la machine de Nick.
- Un `FantasyWarrior.Api` ou `.Jobs` qui traîne verrouille la sortie de build.

---

## Ce qu'il faut de Nick

1. **Secret GitHub `AZURE_SQL_CONNECTION`** — sans lui aucun workflow ne tourne.
2. **Pare-feu pour les runners GitHub Actions** (IP dynamiques) : étape
   `az sql server firewall-rule create` en début de workflow.
3. **Trancher le barème de Les Mordus** : la doc dit « victoire de gardien = 1 »,
   le seed SQL utilise **2** (comme Firestore le faisait réellement).

---

## Historique des sessions

### 2026-08-01 / 08-02 — mise en place et cœur du backend

Analyse, validation de la vision (3 corrections), plan approuvé, puis phases 0,
1, 5 complètes, 3 et 4 largement, CapWages, et la validation contre l'oracle.

| Commit | |
|---|---|
| `fee4d8c` | Échanges à la frontière de période |
| `f76b990` | `dump-golden` — l'oracle |
| `f761754` | Phase 1 : le schéma Azure SQL |
| `781848b` | `capwages-sync` — les vrais contrats |
| `f020104` | `player-sync` sur EF + chargement d'Azure SQL |
| `605c26e` | Plan + suivi rapatriés dans le dépôt |
| `394a404` | Credentials via appsettings |
| `ec1f7b2` | Ingestion + pointage sur SQL, saison ré-importée |
| `30e3f26` | Seed Les Mordus, alignement d'ouverture, rien de scoré |
| `06aa225` | `sim-advance` + preuve du moteur contre l'oracle |
