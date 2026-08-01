# Migration Azure SQL — suivi

> **Le fichier à lire pour reprendre le chantier.** Le plan cible est dans
> [sql-migration-plan.md](sql-migration-plan.md) et ne bouge pas ; celui-ci
> suit l'avancement réel et doit être mis à jour à chaque session.
>
> Dernière mise à jour : **2026-08-01** (Macklin Softwarini)
> Branche : **`sql-migration`** (partie de `main` à `d9c8527`)

---

## Où on en est

```
Phase 0  Filet de sécurité + infra    ████████████████████  fait
Phase 1  Projet Data (schéma)         ████████████████████  fait
Phase 2  Core dé-Firestorisé          ░░░░░░░░░░░░░░░░░░░░  pas commencé
Phase 3  Ingestion                    ██████░░░░░░░░░░░░░░  1 job sur 5
Phase 4  Écritures pool               ░░░░░░░░░░░░░░░░░░░░  pas commencé
Phase 5  Pointage                     ░░░░░░░░░░░░░░░░░░░░  pas commencé
Phase 6  API                          ░░░░░░░░░░░░░░░░░░░░  pas commencé
Phase 7  Simulation                   ░░░░░░░░░░░░░░░░░░░░  pas commencé
Phase 8  Bascule                      ░░░░░░░░░░░░░░░░░░░░  pas commencé
+        CapWages (hors plan initial) ████████████████████  fait
```

**Rien n'est déployé.** La prod tourne toujours sur Cloud Run + Firestore,
inchangée — la branche `sql-migration` n'est pas fusionnée et le frontend
continue de parler à l'ancienne API. Aucun risque de régression pour Nick tant
que ça reste vrai.

---

## Ce qui vit dans Azure SQL en ce moment

| | |
|---|---|
| Serveur / base | `fantasywarrior.database.windows.net` / `fantasywarrior` |
| Migrations appliquées | 3 (`InitialSchema`, `ScoringViews`, `PlayerCapWagesSlug`) |
| `Players` | **1 275** |
| `PlayerContracts` | **3 269** saisons-contrats, 10 saisons distinctes |
| `NhlTeams` | 32 (semées par la migration) |
| Tout le reste | **vide** — jeux, lignes de match, ligues, équipes, rosters |

Couverture salariale : **685 / 701 joueurs de statut `nhl` ont un vrai cap hit
2025-26 (97,7 %)**. Vérifié : Draisaitl 14,0 M$, Matthews 13,25 M$,
MacKinnon 12,6 M$, McDavid 12,5 M$.

---

## Détail par phase

### ✅ Phase 0 — Filet de sécurité

- Job `dump-golden` (`Jobs/Ops/DumpGoldenJob.cs`), lecture seule sur Firestore.
- **`.claude/doc/golden-scores-preSql.json`** (624 Ko, versionné) : 2 ligues,
  23 équipes, 451 roster spots, 92 alignements, 28 périodes (2 banquées),
  jusqu'au grain **joueur-semaine**. C'est l'oracle de correction du chantier.
- ⚠️ Le curseur de simulation était à **2025-10-20**, donc l'oracle ne couvre
  que les semaines 1 à 3. Suffisant pour valider, mais pas toute la saison.

### ✅ Phase 1 — Projet Data

`backend/FantasyWarrior.Data/` : 20 entités, 4 vues, 3 migrations.
`backend/FantasyWarrior.Data.Tests/` : **19 tests d'intégration** contre un vrai
SQL Server (LocalDB par défaut, `FW_TEST_SQL_CONNECTION` pour pointer ailleurs ;
skip propre si aucun serveur n'est joignable).

Vues : `vPlayerSeasonStats`, `vRosterSpotTotals`, `vTeamPeriodScores`,
`vStandings`. **Tout ce qui est au-dessus du grain « assignment » est dérivé**,
jamais stocké.

Contraintes que la BD applique elle-même : un propriétaire par joueur par ligue
(index unique filtré), un assignment par spot par semaine, un actif d'échange
= joueur **ou** pick, un vote « équitable » sans intensité, un seul curseur de
simulation.

### ⬜ Phase 2 — Core dé-Firestorisé

**Pas commencé.** `FantasyWarrior.Core` porte encore `[FirestoreData]` et la
référence au paquet `Google.Cloud.Firestore`.

À faire :
- Supprimer les entités Firestore de Core : `League`, `Team`, `RosterSpot`,
  `Trade`, `Lineup`, `NewsItem`, `Period`, `Player`, `Game`, `PlayerGameStats`,
  `PlayerSeasonStats`, `User`, `SimulationState`.
- Supprimer `PlayerTotalsSource` (remplacé par `vPlayerSeasonStats`),
  `SeasonStatsAdvance` (le problème disparaît avec la vue), `RosterSpots`,
  `RosterChange` (requêtes Firestore).
- Garder et purifier : `StatLine`, `StatKeys`, `StatWindow`, `PeriodScoring`,
  `RuleConfig`, `ScoringEngine`, `StatLineAdapters`, `LineupRules`,
  `PeriodCalendar`, `NameNormalizer`, `PoolClock`, `TradeValidation`.
- **`StatWindow` et `PeriodScoring` passent de `string` à `DateOnly`.** Le tri
  ordinal sur chaîne était une contrainte Firestore ; les colonnes SQL sont des
  `date`. (`StatWindow.Intersect` est déjà réécrit en `DateOnly` dans un brouillon
  local non commité — à refaire ou à récupérer.)
- `StatLine.FromGameLine` disparaît de Core : l'équivalent est
  `FantasyWarrior.Data.StatColumns.ToStatLine`.

⚠️ **Piège de séquencement** : `FantasyWarrior.Core.Tests` référence
`FantasyWarrior.Jobs`, donc vider Core casse aussi la compilation des tests tant
que Jobs n'est pas porté. Deux options : porter Jobs dans la même passe, ou
sortir `StatsSyncJobTests`/`CapWagesParserTests` de Core.Tests d'abord.

### 🔶 Phase 3 — Ingestion (1 job sur 5)

| Job | État | Note |
|---|---|---|
| `sql-player-sync` | ✅ fait | `Jobs/Sql/PlayerSyncJob.cs`, exécuté, 1 275 joueurs |
| `stats-sync` | ⬜ | **le gros morceau** — ré-importer 1 342 matchs / ~51 264 lignes |
| `period-init` | ⬜ | 28 semaines dérivées des `Games` |
| `draft-sync` | ⬜ | 1 appel HTTP par joueur non encore vérifié |
| `news-sync` | ⬜ | 3 sources, `HtmlAgilityPack` déjà en place |

Commande de ré-import une fois `stats-sync` porté :
`stats-sync --from 2025-10-07 --to 2026-04-16` puis `period-init --season 20252026`.
Attendu : **1 342 matchs, ~51 264 lignes, 28 semaines dont 2 mortes**
(olympiques, 9–22 février 2026).

### ⬜ Phases 4 à 8

Pas commencées. Voir le plan pour le détail.

### ✅ CapWages (ajouté en cours de route)

`Jobs/CapWages/` : `CapWagesParser` (pur, testé), `CapWagesClient` (HTTP poli),
`CapWagesSyncJob`. Commande `capwages-sync [--season] [--dry-run]
[--resolve-unmatched]`.

Remplace `estimate-salaries` (chiffres inventés depuis juillet). Voir les
amendements du plan pour pourquoi c'est du JSON embarqué et pas du HTML.

Fixtures réelles capturées le 2026-08-01 dans
`backend/FantasyWarrior.Core.Tests/Fixtures/` — **à recapturer et differ** si un
jour un run revient vide.

---

## Conventions de ce chantier

- **Préfixe `sql-`** : un job porté cohabite avec son ancêtre Firestore sous
  `Jobs/Sql/` et s'invoque `sql-player-sync`. La bascule (phase 8) supprime les
  anciens et laisse tomber le préfixe.
- **Le frontend ne bouge pas.** Les réponses JSON doivent rester identiques au
  champ près. `league.id` sera le `JoinCode` (chaîne courte), `spotId` le
  `RosterSpotId` en chaîne — le frontend traite les deux comme opaques
  (vérifié dans `api.ts`, `App.tsx`, `Stats.tsx`).
- **Un seul grain honnête** : `RosterAssignment`. Tout total au-dessus est une
  vue. Ne jamais rajouter de colonne de score sur `Teams`.
- Migrations appliquées par la commande `db-migrate`, **jamais au démarrage de
  l'API** (Cloud Run peut lancer plusieurs instances en parallèle).

---

## Environnement local

```powershell
# La chaîne de connexion (hors dépôt, à côté de la clé Firebase)
$env:AZURE_SQL_CONNECTION = Get-Content "C:\Nick\secrets\azure-sql-connection.txt" -Raw

dotnet run --project backend/FantasyWarrior.Jobs -- db-migrate --list
dotnet run --project backend/FantasyWarrior.Jobs -- sql-player-sync --season 20252026
dotnet run --project backend/FantasyWarrior.Jobs -- capwages-sync --dry-run
dotnet test FantasyWarrior.slnx        # 200 Core + 19 Data
```

- **`dotnet-ef` 10.0.10** installé en outil global.
- Les tests d'intégration utilisent **LocalDB** (`MSSQLLocalDB`), pas Azure —
  ils créent et détruisent `FantasyWarriorTests` à chaque run.
- L'API NHL (`api-web.nhle.com`) et `capwages.com` sont **joignables** depuis la
  machine de Nick. (Elles ne l'étaient pas dans les sandboxes des sessions
  précédentes — d'où les vérifications live enfin possibles.)
- Un `FantasyWarrior.Api` local qui traîne verrouille la sortie de build ; le
  tuer avant de compiler.

---

## Ce qu'il faut de Nick

1. **Secret GitHub `AZURE_SQL_CONNECTION`** — sans lui, aucun workflow ne peut
   tourner contre la nouvelle base.
2. **Règle de pare-feu pour les runners GitHub Actions.** Leurs IP sont
   dynamiques : prévoir une étape `az sql server firewall-rule create` en début
   de workflow (et sa suppression en fin) plutôt que d'ouvrir `0.0.0.0`.
3. **Trancher le barème de Les Mordus** (voir ci-dessous).

---

## Trouvailles à traiter

- **Les Mordus n'a jamais eu le barème documenté.** `scoring-model.md` et
  `project_status.md` annoncent « victoire de gardien = 1 », mais la vraie
  config en base est **2** (la valeur par défaut) — personne ne l'a changée.
  Visible dans `golden-scores-preSql.json`. **À trancher avant de reseeder**,
  sinon l'oracle et la nouvelle base seront comparés sur deux barèmes.
- **Cold start Azure** : ~10 s pour reprendre après pause. Acceptable pour les
  jobs, discutable pour une requête utilisateur. Point à rouvrir en phase 6.
- **499 joueurs CapWages non appariés** : AHL et profondeur, que les endpoints
  NHL ne retournent pas. Attendu, pas un bug — mais si ce nombre explose un
  jour, c'est le signal que l'appariement est cassé.

---

## Historique des sessions

### 2026-08-01 — mise en place

Analyse de l'existant, validation de la vision de Nick (3 corrections : un seul
grain pour les points, season stats en vue, Period/Game manquants), plan
approuvé, puis phases 0 et 1 complètes + CapWages + `player-sync`.

Commits sur `sql-migration` :

| | |
|---|---|
| `fee4d8c` | Échanges exécutés à la frontière de période (travail d'une session antérieure, complété) |
| `f76b990` | `dump-golden` — l'oracle de correction |
| `f761754` | Phase 1 : le schéma Azure SQL |
| `781848b` | `capwages-sync` — les vrais contrats |
| `f020104` | `player-sync` sur EF + chargement réel d'Azure SQL |
