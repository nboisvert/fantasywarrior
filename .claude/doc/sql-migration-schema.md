# Schéma relationnel — migration Firestore → Azure SQL (conception)

> Étape 2 de l'évaluation Firestore → SQL (étape 1 : choix d'hébergement, voir la
> conversation du 2026-08-01 — recommandation retenue : **Azure SQL Database, free
> offer**, 32 GB / T-SQL natif). Ce document est la **conception du schéma**, pas
> encore une migration exécutée : rien n'a été implémenté, aucun code backend n'a
> changé. À lire avant d'écrire la moindre ligne de DDL/EF Core réelle.
>
> Cible : **T-SQL (Azure SQL Database / SQL Server)**. Types et syntaxe ci-dessous
> sont du T-SQL valide.

## Principe directeur

Beaucoup de champs Firestore actuels existent **uniquement pour éviter des lectures
payantes** (le plan gratuit facture par lecture de document) : `Team.PlayerIds`,
`Team.PlayerPoints`, `Team.PlayerNhlPoints`, `PlayerSeasonStats` au complet, les
rollups sur `RosterSpot`/`Team` (`ActivePoints`, `FinalizedScore`, etc.). En SQL, un
`JOIN`/`GROUP BY` indexé coûte quasiment rien — donc ce schéma **distingue
explicitement** :

- **Données sources** (viennent du monde extérieur — NHL API, un GM qui clique) →
  tables physiques, normalisées.
- **Données dérivées** (recalculables à 100 % à partir d'autres tables) → idéalement
  des **vues**, pas des colonnes dupliquées à maintenir.

Le schéma ci-dessous reste **fidèle au modèle actuel** (une table par collection,
mêmes champs) pour rester un mapping direct, sûr et exécutable — mais chaque champ
purement dérivé est marqué **[DÉRIVÉ]** avec la requête qui le remplacerait. Décider
lesquels éliminer vraiment est un choix séparé, pas fait ici.

## Conventions

- **Clés** : quand le doc id Firestore était déjà un identifiant métier stable
  (`nhlId`, `nhlGameId`, `season+index`, `season+playerId`, `gameId+playerId`,
  username normalisé) → gardé comme clé primaire naturelle. Quand c'était un id
  Firestore auto-généré sans signification (`rosterSpots`, `trades`) → `BIGINT
  IDENTITY`.
- **Dates** : tous les champs `"YYYY-MM-DD"` (string) deviennent `DATE` natif — fini
  les comparaisons de chaînes pour les plages de dates (`StatWindow.Intersect`,
  `RelevantToAsync`, etc. deviennent des `WHERE date BETWEEN ...` normaux).
- **Timestamps** : tous les champs `XxxUtc` → `DATETIME2(3)`, toujours en UTC (le nom
  le dit déjà, pas besoin de `DATETIMEOFFSET`).
- **Maps ouvertes** (`Dictionary<string, X>` à clés non fixes — `ExtraPointValues`,
  `ActiveStats`, `LineupResult.Stats`) → colonne `NVARCHAR(MAX)` avec contrainte
  `CHECK (ISJSON(...) = 1)`, interrogeable via `JSON_VALUE`/`OPENJSON`. C'est
  l'équivalent SQL du côté "schéma flexible" de Firestore que `StatKeys` exploite
  déjà (un commissaire peut scorer un nouveau stat sans migration).
- **Argent** : `capHit`/`capAmount` restent `BIGINT` (cents ou dollars entiers, comme
  aujourd'hui).
- **Points** : `DECIMAL(10,2)` partout où Firestore avait `double` pour un score (évite
  la dérive en virgule flottante sur des cumuls qui ne bougent plus une fois banqués).

## Mapping collections Firestore → tables SQL

| Collection Firestore | Table(s) SQL | Note |
|---|---|---|
| `players` | `players` | 1:1 |
| `games` | `games` | 1:1 |
| `playerGameStats` | `player_game_stats` | 1:1, la plus grosse table |
| `playerSeasonStats` | `player_season_stats` | cache write-through conservé tel quel (voir "Simplifications" plus bas) |
| `periods` | `periods` | 1:1, PK naturelle (season, index) |
| `users` | `users` | 1:1, PK = username normalisé |
| `leagues/{id}` | `leagues` | `memberUsernames` **abandonné** — dérivable de `teams` |
| `leagues/{id}/teams/{username}` | `teams` | `playerIds`/`playerPoints`/`playerNhlPoints` **[DÉRIVÉ]** |
| `leagues/{id}/rosterSpots` | `roster_spots` | id Firestore opaque → `IDENTITY` |
| `leagues/{id}/lineups` | `lineups` + `lineup_active_spots` + `lineup_results` | `ActiveSpotIds`/`Results` normalisés en tables filles |
| `leagues/{id}/trades` | `trades` + `trade_players` | listes de joueurs normalisées |
| `leagues/{id}/trades/{id}/votes` | `trade_votes` | 1:1 |
| `news` | `news` | 1:1, PK = hash source+guid (conserve l'upsert idempotent) |

---

## DDL

```sql
-- ============================================================
-- Référentiel joueurs / calendrier NHL (global, partagé entre ligues)
-- ============================================================

CREATE TABLE players (
    nhl_id              BIGINT          NOT NULL PRIMARY KEY,
    first_name          NVARCHAR(100)   NOT NULL,
    last_name           NVARCHAR(100)   NOT NULL,
    position            CHAR(1)         NOT NULL,      -- C, L, R, D, G
    team_abbrev         CHAR(3)         NOT NULL,
    status              VARCHAR(10)     NOT NULL,      -- 'nhl' | 'prospect'
    sweater_number      SMALLINT        NULL,
    shoots_catches      CHAR(1)         NULL,
    birth_date          DATE            NULL,
    birth_country       CHAR(3)         NULL,
    height_cm           SMALLINT        NULL,
    weight_kg           SMALLINT        NULL,
    headshot_url        NVARCHAR(500)   NULL,
    draft_year          SMALLINT        NULL,
    draft_round         TINYINT         NULL,
    draft_overall       SMALLINT        NULL,
    draft_team_abbrev   CHAR(3)         NULL,
    draft_checked       BIT             NOT NULL DEFAULT 0,
    cap_hit             BIGINT          NULL,          -- USD annuel, null = pas encore importé
    last_synced_utc     DATETIME2(3)    NOT NULL
);
CREATE INDEX ix_players_team ON players(team_abbrev);
CREATE INDEX ix_players_status ON players(status);

CREATE TABLE games (
    nhl_game_id       BIGINT        NOT NULL PRIMARY KEY,
    season            CHAR(8)       NOT NULL,      -- "20252026"
    game_type         TINYINT       NOT NULL,      -- 2 = régulière, 3 = séries
    game_date         DATE          NOT NULL,
    home_abbrev       CHAR(3)       NOT NULL,
    away_abbrev       CHAR(3)       NOT NULL,
    home_score        TINYINT       NOT NULL,
    away_score        TINYINT       NOT NULL,
    last_period_type  CHAR(3)       NOT NULL,      -- REG, OT, SO
    synced_utc        DATETIME2(3)  NOT NULL
);
CREATE INDEX ix_games_season_date ON games(season, game_date);

-- Table la plus volumineuse (~51k lignes/saison). PK composite = doc id Firestore
-- "{gameId}_{playerId}". Champs skater/goalie nullable, comme en C#.
CREATE TABLE player_game_stats (
    game_id            BIGINT         NOT NULL REFERENCES games(nhl_game_id),
    player_id          BIGINT         NOT NULL REFERENCES players(nhl_id),
    game_date          DATE           NOT NULL,  -- dénormalisé : colonne de la requête nocturne par plage de dates
    season             CHAR(8)        NOT NULL,
    game_type          TINYINT        NOT NULL,
    name               NVARCHAR(150)  NOT NULL,  -- snapshot au moment du match
    team_abbrev        CHAR(3)        NOT NULL,
    opponent_abbrev    CHAR(3)        NOT NULL,
    position           CHAR(1)        NOT NULL,
    is_goalie          BIT            NOT NULL,
    is_home            BIT            NOT NULL,
    toi                CHAR(5)        NOT NULL,  -- "MM:SS"
    pim                TINYINT        NOT NULL,
    -- skaters
    goals              TINYINT        NULL,
    assists            TINYINT        NULL,
    points             TINYINT        NULL,
    plus_minus         SMALLINT       NULL,
    shots              TINYINT        NULL,
    hits               TINYINT        NULL,
    blocked_shots      TINYINT        NULL,
    power_play_goals   TINYINT        NULL,
    -- goalies
    shots_against      SMALLINT       NULL,
    saves              SMALLINT       NULL,
    goals_against      TINYINT        NULL,
    decision           CHAR(1)        NULL,  -- W, L, O
    starter            BIT            NULL,
    shutout            BIT            NULL,
    ot_loss            BIT            NULL,
    synced_utc         DATETIME2(3)   NOT NULL,
    PRIMARY KEY (game_id, player_id)
);
CREATE INDEX ix_pgs_date ON player_game_stats(game_date);           -- requête nocturne (plage de dates, toutes ligues)
CREATE INDEX ix_pgs_player ON player_game_stats(player_id, season); -- page joueur / season stats

-- Cache write-through, conservé tel quel (voir "Simplifications" plus bas pour
-- l'alternative "vue calculée").
CREATE TABLE player_season_stats (
    season         CHAR(8)       NOT NULL,
    player_id      BIGINT        NOT NULL REFERENCES players(nhl_id),
    games_played   SMALLINT      NOT NULL DEFAULT 0,
    goals          SMALLINT      NOT NULL DEFAULT 0,
    assists        SMALLINT      NOT NULL DEFAULT 0,
    plus_minus     SMALLINT      NOT NULL DEFAULT 0,
    pim            SMALLINT      NOT NULL DEFAULT 0,
    shots          SMALLINT      NOT NULL DEFAULT 0,
    hits           SMALLINT      NOT NULL DEFAULT 0,
    blocked_shots  SMALLINT      NOT NULL DEFAULT 0,
    wins           SMALLINT      NOT NULL DEFAULT 0,
    ot_losses      SMALLINT      NOT NULL DEFAULT 0,
    shutouts       SMALLINT      NOT NULL DEFAULT 0,
    goals_against  SMALLINT      NOT NULL DEFAULT 0,
    saves          SMALLINT      NOT NULL DEFAULT 0,
    shots_against  SMALLINT      NOT NULL DEFAULT 0,
    through_date   DATE          NULL,
    updated_utc    DATETIME2(3)  NOT NULL,
    PRIMARY KEY (season, player_id)
);

-- Global, partagé entre toutes les ligues (voir scoring-model.md §7 — c'est ce qui
-- permet à la job nocturne de ne faire qu'une requête par plage de dates).
CREATE TABLE periods (
    season         CHAR(8)       NOT NULL,
    period_index   TINYINT       NOT NULL,  -- 1-based, ~28/saison
    start_date     DATE          NOT NULL,  -- lundi (sauf 1re semaine)
    end_date       DATE          NOT NULL,  -- dimanche (sauf dernière semaine)
    lock_utc       DATETIME2(3)  NOT NULL,
    game_count     SMALLINT      NOT NULL DEFAULT 0,
    finalized_utc  DATETIME2(3)  NULL,
    created_utc    DATETIME2(3)  NOT NULL,
    PRIMARY KEY (season, period_index)
);

-- ============================================================
-- Comptes
-- ============================================================

CREATE TABLE users (
    username         VARCHAR(50)    NOT NULL PRIMARY KEY,  -- normalisé (lowercase, trim)
    display_name     NVARCHAR(100)  NOT NULL,
    created_utc      DATETIME2(3)   NOT NULL,
    last_login_utc   DATETIME2(3)   NOT NULL
);

-- ============================================================
-- Ligues (racine multi-tenant)
-- ============================================================

CREATE TABLE leagues (
    league_id                INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    invite_code              VARCHAR(20)     NOT NULL UNIQUE,  -- ex-doc-id Firestore ; sert de code d'invitation
    name                     NVARCHAR(100)   NOT NULL,
    season                   CHAR(8)         NOT NULL,
    commissioner_username    VARCHAR(50)     NOT NULL REFERENCES users(username),
    cap_amount               BIGINT          NULL,  -- USD/équipe, null = pas de plafond
    -- RuleConfig aplati : les 5 valeurs fixes de PointValues
    point_goal               DECIMAL(6,2)    NOT NULL DEFAULT 1,
    point_assist             DECIMAL(6,2)    NOT NULL DEFAULT 1,
    point_goalie_win         DECIMAL(6,2)    NOT NULL DEFAULT 2,
    point_goalie_ot_loss     DECIMAL(6,2)    NOT NULL DEFAULT 1,
    point_shutout            DECIMAL(6,2)    NOT NULL DEFAULT 0,
    -- ExtraPointValues : map ouverte (StatKeys), JSON par nature
    extra_point_values       NVARCHAR(MAX)   NULL CHECK (extra_point_values IS NULL OR ISJSON(extra_point_values) = 1),
    top_forwards             SMALLINT        NULL,  -- null = tout le monde compte
    top_defense              SMALLINT        NULL,
    top_goalies              SMALLINT        NULL,
    roster_min               SMALLINT        NULL,  -- affiché seulement, jamais validé
    roster_max               SMALLINT        NULL,
    created_utc               DATETIME2(3)    NOT NULL
);
-- Note : le `memberUsernames` de Firestore (array-contains pour "mes ligues") est
-- ABANDONNÉ ici — dérivable directement : SELECT l.* FROM leagues l
-- JOIN teams t ON t.league_id = l.league_id WHERE t.owner_username = @username.

CREATE TABLE teams (
    league_id                        INT            NOT NULL REFERENCES leagues(league_id),
    owner_username                   VARCHAR(50)    NOT NULL REFERENCES users(username),
    name                             NVARCHAR(100)  NOT NULL,
    franchise_abbrev                 CHAR(3)        NULL,  -- identité NHL permanente (ex. "Les Mordus")
    created_utc                      DATETIME2(3)   NOT NULL,
    -- rollups pointage hebdo (scoring-model.md) — invariant : score = finalized_score + period_points
    finalized_through_period_index   SMALLINT       NOT NULL DEFAULT 0,
    finalized_score                  DECIMAL(10,2)  NOT NULL DEFAULT 0,  -- banqué, ne bouge plus jamais
    period_points                    DECIMAL(10,2)  NOT NULL DEFAULT 0,  -- semaine en cours, recalculé à zéro chaque nuit
    bench_score                      DECIMAL(10,2)  NOT NULL DEFAULT 0,
    current_period_index             SMALLINT       NOT NULL DEFAULT 0,
    roster_games_played              INT            NOT NULL DEFAULT 0,  -- [DÉRIVÉ] SUM(games_played) via roster_spots ouverts
    cap_total                        BIGINT         NOT NULL DEFAULT 0,  -- [DÉRIVÉ] SUM(players.cap_hit) sur le roster courant
    score_updated_utc                DATETIME2(3)   NULL,
    PRIMARY KEY (league_id, owner_username)
);
-- Champs Firestore ABANDONNÉS ici, tous [DÉRIVÉ] :
--   playerIds       -> roster_spots WHERE end_date IS NULL
--   playerPoints    -> SUM(lineup_results.points) GROUP BY player_id
--   playerNhlPoints -> player_season_stats jointe aux roster_spots ouverts
--   periodScores    -> SUM(lineup_results.points) GROUP BY period_index
-- score (affiché) = finalized_score + period_points, calculable en vue ou en colonne
-- calculée : ADD score AS (finalized_score + period_points) PERSISTED.

-- ============================================================
-- Roster spots — l'appartenance d'un joueur à une équipe, jamais supprimée
-- ============================================================

CREATE TABLE roster_spots (
    id                                BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    league_id                         INT            NOT NULL REFERENCES leagues(league_id),
    player_id                         BIGINT         NOT NULL REFERENCES players(nhl_id),
    team_username                     VARCHAR(50)    NOT NULL,
    position                          CHAR(1)        NOT NULL,  -- code NHL brut
    position_group                    CHAR(1)        NOT NULL,  -- F/D/G, figé à l'ouverture
    start_date                        DATE           NOT NULL,
    start_reason                      VARCHAR(10)    NOT NULL DEFAULT 'freeagent',  -- draft|trade|freeagent
    start_ref_id                      VARCHAR(50)    NULL,
    end_date                          DATE           NULL,  -- null = toujours détenu
    end_reason                        VARCHAR(10)    NULL,  -- trade|release
    end_ref_id                        VARCHAR(50)    NULL,
    opened_utc                        DATETIME2(3)   NOT NULL,
    closed_utc                        DATETIME2(3)   NULL,
    -- rollups, écrits par la job de pointage
    finalized_through_period_index    SMALLINT       NOT NULL DEFAULT 0,
    finalized_active_points           DECIMAL(10,2)  NOT NULL DEFAULT 0,
    finalized_bench_points            DECIMAL(10,2)  NOT NULL DEFAULT 0,
    active_points                     DECIMAL(10,2)  NOT NULL DEFAULT 0,
    bench_points                      DECIMAL(10,2)  NOT NULL DEFAULT 0,
    active_stats                      NVARCHAR(MAX)  NULL CHECK (active_stats IS NULL OR ISJSON(active_stats) = 1),  -- StatLine, map ouverte
    active_games_played               SMALLINT       NOT NULL DEFAULT 0,
    rollup_updated_utc                DATETIME2(3)   NULL,
    FOREIGN KEY (league_id, team_username) REFERENCES teams(league_id, owner_username)
);
-- Remplace les DEUX requêtes Firestore (endDate==null) UNION (endDate>=periodStart)
-- de RosterSpots.RelevantToAsync — en SQL c'est une seule requête :
--   WHERE league_id = @l AND (end_date IS NULL OR end_date >= @periodStart)
CREATE INDEX ix_rs_league_open ON roster_spots(league_id, end_date);
CREATE INDEX ix_rs_team_open ON roster_spots(league_id, team_username, end_date);
CREATE INDEX ix_rs_player ON roster_spots(player_id);

-- ============================================================
-- Lineups — un doc par équipe par semaine ; ActiveSpotIds/Results normalisés
-- ============================================================

CREATE TABLE lineups (
    league_id       INT           NOT NULL REFERENCES leagues(league_id),
    team_username   VARCHAR(50)   NOT NULL,
    period_index    SMALLINT      NOT NULL,
    season          CHAR(8)       NOT NULL,
    -- champ GM
    submitted_utc   DATETIME2(3)  NULL,
    set_by          VARCHAR(20)   NOT NULL DEFAULT 'auto',  -- username ou "auto"
    -- champs job
    active_points   DECIMAL(10,2) NOT NULL DEFAULT 0,
    bench_points    DECIMAL(10,2) NOT NULL DEFAULT 0,
    scored_utc      DATETIME2(3)  NULL,
    PRIMARY KEY (league_id, team_username, period_index),
    FOREIGN KEY (league_id, team_username) REFERENCES teams(league_id, owner_username),
    FOREIGN KEY (season, period_index) REFERENCES periods(season, period_index)
);

-- Remplace Lineup.ActiveSpotIds (List<string>, 1 champ = 1 write atomique côté
-- Firestore pour éviter une transaction). En SQL, on obtient la même atomicité en
-- enveloppant le "submit" dans une transaction (DELETE + INSERT), donc on peut
-- normaliser proprement.
CREATE TABLE lineup_active_spots (
    league_id        INT          NOT NULL,
    team_username    VARCHAR(50)  NOT NULL,
    period_index     SMALLINT     NOT NULL,
    roster_spot_id   BIGINT       NOT NULL REFERENCES roster_spots(id),
    PRIMARY KEY (league_id, team_username, period_index, roster_spot_id),
    FOREIGN KEY (league_id, team_username, period_index) REFERENCES lineups(league_id, team_username, period_index)
);

-- Remplace Lineup.Results (Dictionary<spotId, LineupResult>) — inclut les joueurs
-- au banc, ce qui permet "N points laissés au banc".
CREATE TABLE lineup_results (
    league_id         INT            NOT NULL,
    team_username     VARCHAR(50)    NOT NULL,
    period_index      SMALLINT       NOT NULL,
    roster_spot_id    BIGINT         NOT NULL REFERENCES roster_spots(id),
    player_id         BIGINT         NOT NULL REFERENCES players(nhl_id),
    position_group    CHAR(1)        NOT NULL,
    points            DECIMAL(10,2)  NOT NULL DEFAULT 0,
    games_played      SMALLINT       NOT NULL DEFAULT 0,
    stats             NVARCHAR(MAX)  NULL CHECK (stats IS NULL OR ISJSON(stats) = 1),  -- StatLine, map ouverte
    from_date         DATE           NOT NULL,
    to_date           DATE           NOT NULL,
    PRIMARY KEY (league_id, team_username, period_index, roster_spot_id),
    FOREIGN KEY (league_id, team_username, period_index) REFERENCES lineups(league_id, team_username, period_index)
);

-- ============================================================
-- Trades
-- ============================================================

CREATE TABLE trades (
    id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    league_id                INT           NOT NULL REFERENCES leagues(league_id),
    proposer_username        VARCHAR(50)   NOT NULL,
    counterparty_username    VARCHAR(50)   NOT NULL,
    status                   VARCHAR(12)   NOT NULL DEFAULT 'pending',  -- pending|declined|cancelled|accepted|processed
    created_utc              DATETIME2(3)  NOT NULL,
    responded_utc            DATETIME2(3)  NULL,
    processed_utc            DATETIME2(3)  NULL,
    FOREIGN KEY (league_id, proposer_username) REFERENCES teams(league_id, owner_username),
    FOREIGN KEY (league_id, counterparty_username) REFERENCES teams(league_id, owner_username)
);

-- Remplace PlayersFromProposer/PlayersFromCounterparty (2 listes). PK sur
-- (trade_id, player_id) SANS le côté dans la clé : un joueur ne peut alors
-- apparaître qu'une fois par trade, quel que soit le côté — TradeValidation.HasOverlap
-- devient une contrainte de la base plutôt qu'une vérification applicative.
CREATE TABLE trade_players (
    trade_id     BIGINT       NOT NULL REFERENCES trades(id),
    player_id    BIGINT       NOT NULL REFERENCES players(nhl_id),
    side         VARCHAR(12)  NOT NULL,  -- 'proposer' | 'counterparty'
    PRIMARY KEY (trade_id, player_id)
);

CREATE TABLE trade_votes (
    trade_id            BIGINT        NOT NULL REFERENCES trades(id),
    voter_username      VARCHAR(50)   NOT NULL,
    favored_username    VARCHAR(50)   NULL,  -- null = "fair"
    magnitude           TINYINT       NOT NULL DEFAULT 0,  -- 0, 1 (leans), 2 (clearly won)
    voted_utc           DATETIME2(3)  NOT NULL,
    PRIMARY KEY (trade_id, voter_username)
);

-- ============================================================
-- Actualités (globale, non scopée à une ligue)
-- ============================================================

CREATE TABLE news (
    source_key      VARCHAR(64)    NOT NULL PRIMARY KEY,  -- hash déterministe source+guid (upsert idempotent)
    source          VARCHAR(20)    NOT NULL,  -- rotowire_rss | rotowire_html | fantasysp
    headline        NVARCHAR(500)  NOT NULL,
    url             NVARCHAR(500)  NOT NULL,
    player_id       BIGINT         NULL REFERENCES players(nhl_id),
    player_name     NVARCHAR(150)  NULL,
    published_utc   DATETIME2(3)   NOT NULL,
    fetched_utc     DATETIME2(3)   NOT NULL
);
CREATE INDEX ix_news_fetched ON news(fetched_utc);  -- purge de rétention 30 jours
CREATE INDEX ix_news_player ON news(player_id);
```

> **Note FK `lineups.period_index`** : la FK vers `periods(season, period_index)`
> ci-dessus référence `(league_id, period_index)` mais `periods` est global (clé
> `season, period_index`) — la vraie contrainte devrait être sur `(season,
> period_index)` de `lineups`, pas `league_id`. Détail à corriger au moment
> d'écrire le DDL final (`lineups.season` existe déjà, il suffit de la FK sur les
> bonnes colonnes) — laissé tel quel ici pour ne pas alourdir la lecture du
> brouillon.

---

## Simplifications que SQL rend possibles (à évaluer, pas décidé)

Ce ne sont **pas** des changements à faire maintenant — juste ce que ce nouveau
moteur permettrait, pour que la décision soit informée :

1. **`player_season_stats` pourrait devenir une vue** plutôt qu'une table
   maintenue par une job d'écriture :
   ```sql
   CREATE VIEW vw_player_season_stats AS
   SELECT season, player_id, COUNT(*) AS games_played,
          SUM(ISNULL(goals,0)) AS goals, SUM(ISNULL(assists,0)) AS assists, ...
   FROM player_game_stats GROUP BY season, player_id;
   ```
   Élimine toute la logique "cache write-through" (`PlayerTotalsSource`,
   `SeasonStatsAdvance`) — un `GROUP BY` indexé sur ~38 lignes/joueur est trivial
   pour SQL. Compromis : une vraie vue non matérialisée recalcule à chaque lecture
   (négligeable à cette échelle) ; une *vue indexée* (materialized) serait
   l'équivalent SQL Server d'un cache si jamais ça devenait nécessaire.
2. **`teams.player_ids`/`player_points`/`player_nhl_points`** (déjà retirés du
   schéma ci-dessus) : entièrement dérivables de `roster_spots` + `lineup_results`
   + `player_season_stats`. Zéro risque de désynchronisation entre le cache et la
   source, contrairement à Firestore où un bug d'écriture peut laisser le cache
   périmé silencieusement.
3. **`RosterSpots.RelevantToAsync`** (deux requêtes Firestore unionnées à la main
   à cause de l'absence de OR inter-opérateurs) devient une seule requête SQL avec
   un `OR` — déjà noté dans le DDL.
4. **`TradeValidation.HasOverlap`** devient une contrainte de clé primaire plutôt
   qu'une vérification applicative après lecture — déjà appliqué dans `trade_players`.

## Ce qui reste inchangé côté logique applicative

Le moteur de calcul (`ScoringEngine`, `StatWindow.Intersect`, `PeriodScoring`,
`SeasonStatsAdvance`) reste une logique **C# pure**, testée sans mock — rien dans ce
schéma ne force à la réécrire en SQL (procédures stockées, etc.). L'app continuerait
de lire/écrire via EF Core comme avec le SDK Firestore aujourd'hui ; seule la couche
de persistance change.

## Prochaines étapes (hors scope de ce document)

- Choix de l'ORM : EF Core (Npgsql pour Postgres ou `Microsoft.EntityFrameworkCore.SqlServer`
  pour Azure SQL) — probable vu le stack .NET.
- Stratégie de migration des données existantes (export Firestore → import SQL,
  une seule fois, pas de double-écriture envisagée pour un projet solo en dev).
- Mettre à jour `.claude/doc/deployment.md` et `CLAUDE.md` (section Stack) une fois
  la bascule décidée et amorcée — **pas encore fait**, cette conception ne change
  rien au stack déclaré tant que Nick ne confirme pas qu'on procède.
