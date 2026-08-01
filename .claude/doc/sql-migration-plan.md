# Repositionnement backend : Firestore → Azure SQL + EF Core

> **Le plan approuvé par Nick le 2026-08-01.** Ce document décrit la *cible* et
> ne change pas au fil des sessions — sauf pour la section « Amendements » à la
> fin, qui consigne les décisions prises après l'approbation.
>
> **L'avancement réel vit ailleurs : [sql-migration-status.md](sql-migration-status.md).**
> C'est ce fichier-là qu'il faut lire pour savoir où on en est et quoi faire ensuite.

## Contexte

Fantasy Warrior tourne aujourd'hui sur Firestore. Le UI est jugé bon et ne doit pas
bouger. Nick a créé une BD Azure SQL (plan gratuit) et veut refaire tout le backend
dessus, en repartant de zéro côté données.

Ce n'est pas un caprice technique. Le modèle actuel est structuré **autour d'une
contrainte de coût** : 50 000 lectures/jour au tier gratuit Firestore. Presque toute
la complexité résiduelle du backend existe pour la contourner :

| Mécanisme actuel | Pourquoi il existe | Devient quoi en SQL |
|---|---|---|
| `playerSeasonStats` + `throughDate` + `FetchWithCacheAsync` | éviter de rescanner 51k lignes | un `GROUP BY` avec index |
| `Team.playerIds` / `playerPoints` / `playerNhlPoints` / `capTotal` / `rosterGamesPlayed` | éviter les jointures inexistantes | des jointures |
| `Team.score` = `finalizedScore + periodPoints`, invariant tenu à la main | pas d'agrégat serveur | `SUM()` |
| `Lineup` = 1 doc/équipe/semaine avec une map `results` | 250 docs au lieu de 5 000 | une vraie table de lignes |
| `RosterSpots.RelevantToAsync` = union de 2 requêtes | Firestore n'a pas de OR | un `WHERE ... OR ...` |
| `check-indexes`, compteurs de lectures, avertissement à 10k | budget de lectures | disparaît |
| unicité « un joueur, un propriétaire par ligue » vérifiée par scan applicatif | pas de contrainte | index unique filtré |

**Ce qui n'est PAS un contournement de coût et doit survivre intact** : le modèle de
pointage hebdomadaire ([scoring-model.md](../../../Nick/fw/.claude/doc/scoring-model.md)) —
verrouillage du lundi, points banqués définitivement, transactions au rollover de
période, exclusion des séries. Ce sont des règles de jeu, pas des optimisations.

**Le point clé qui rend ce chantier faisable** : la logique métier pure est déjà
extraite de la persistance (`StatLine`, `StatWindow`, `LineupRules`, `PeriodScoring`,
`TradeValidation`, `PoolClock`, `RuleConfig`). C'est une réécriture de la couche de
persistance, pas de la logique. Les 152 tests existants survivent quasi tels quels.

---

## Validation de ta vision

Ta modélisation est juste sur l'essentiel. Sept remarques, dont trois corrections.

### ✅ Ce qui est bon tel quel

- **RosterSpot** (start/end date, start/end reference) — c'est exactement le modèle
  actuel, et il est bon. En SQL on gagne : les `refId` stringly-typed deviennent de
  vraies FK (`StartTradeId`, `StartDraftPickId`, `EndTradeId`).
- **RosterAssignment sous RosterSpot** — meilleur que le `Lineup` actuel. Le doc-par-
  équipe-par-semaine était un hack de coût Firestore. Une ligne par (spot, période)
  fait ~9 800 lignes par ligue-saison : trivial en SQL, et **requêtable** (« toutes les
  semaines où Crosby était au banc », « quel GM laisse le plus de points au banc »).
  Ton instinct d'y mettre `from`/`to` en plus du lien vers la période est correct :
  un spot peut ouvrir/fermer en cours de semaine, c'est la fenêtre réellement scorée.
- **DraftPick tradable avec propriétaire distinct de l'origine** — correct, et ça donne
  gratuitement l'affichage « 2e ronde de PIT via BOS ».
- **Trade avec joueurs ET picks** — impose une table `TradeAsset` plutôt que deux
  listes de joueurs. C'est le bon modèle.

### ⚠️ Trois corrections

**1. FantasyPoint : ne pas le stocker à trois grains.**

Tu le décris comme sous-entité de RosterSpot *et* de Team. C'est précisément la
structure qui a déjà causé des bugs dans ce projet (l'invariant
`score = finalizedScore + periodPoints` tenu à la main, le mode shadow, les
recalculs divergents).

Le grain naturel est **un seul : le RosterAssignment** (ce qu'un joueur a produit
pour une équipe pendant une semaine). C'est aussi exactement le grain que la règle
de banquage protège. Tout le reste est un `SUM` :

```
assignment.FantasyPoints          ← calculé une fois, gelé au banquage
  └─ SUM par spot   = ce que ce joueur a rapporté à cette équipe   (vue)
  └─ SUM par équipe = le classement                                 (vue)
  └─ SUM par équipe/période = l'historique hebdo                    (vue)
```

Trois vues SQL, aucune dérive possible, aucun job de synchronisation. Si un jour
le classement devient lent (il ne le sera pas à ~10k lignes), l'échappatoire est
une vue indexée — pas une colonne à maintenir.

**2. Season stats : ne pas en faire une table.**

Tu la décris comme « summarization of all game log for the current year ». En SQL
c'est un `GROUP BY` sur 51k lignes avec un index — quelques millisecondes. La table
n'existe aujourd'hui **que** pour économiser des lectures Firestore.

Et elle règle gratuitement un vrai problème : le mode test a besoin des totaux
*au jour simulé*, ce qui a forcé l'ajout de `throughDate` dans le cache, plus toute
la logique d'invalidation (« une entrée bornée à une autre date n'est pas un hit »).
Un `WHERE GameDate <= @asOf` fait disparaître le problème et son code.

**3. Le calendrier (Period) manque dans ta liste.**

Tu ne le mentionnes qu'en passant (« from, to related to period »), mais c'est une
entité de premier plan : la semaine lundi→dimanche sur la date de match ET, globale
à toutes les ligues, immuable une fois écrite. Idem pour **Game** (le calendrier NHL
brut, dont les périodes sont dérivées). Les deux sont au schéma.

### 📋 Aussi au schéma, non listé chez toi

`NhlTeam` (le slot « Équipe » de Les Mordus a besoin d'une vraie table),
`TradeVote` (le système de notation communautaire existe déjà et doit survivre),
`PlayerContract` (les salaires changent chaque année — une table plutôt qu'une
colonne `capHit`), `PlayerInjury` (tu l'as mentionné : « injury status »),
`NewsItem`, `TeamPeriodLineup` (porte `setBy: auto` — l'UI affiche « alignement
automatique »), et l'état de l'horloge de simulation.

---

## Décisions prises

| Sujet | Décision |
|---|---|
| **Hébergement API** | Tranché plus tard. Le data layer est conçu pour être insensible à l'endroit : peu de requêtes, bien groupées, jamais de N+1 — de façon à survivre à la latence cross-cloud si on garde Cloud Run. |
| **Auth** | Hors scope. On garde le modèle username-trust. `User.ExternalAuthId` est au schéma, non branché. Zéro changement UI. |
| **Portée** | Schéma complet (picks, assets d'échange, contrats, blessures), mais on **ne rebranche que ce que le UI fait déjà**. Repêchage / agence libre / enforcement du cap = chantiers suivants, sans migration de schéma. |
| **Salaires** | Table `PlayerContract` + job d'import CapWages (Nick confirme une option gratuite — il faut la clé et le schéma exact des champs). Les estimations actuelles restent en attendant. |
| **Données** | Repart de zéro. Les données de référence NHL (1 342 matchs, 51 264 lignes de la saison 2025-26) sont **ré-importées** via `stats-sync --from 2025-10-07 --to 2026-04-16`, pas migrées — le job existe déjà et le mode test en dépend. |

---

## Le schéma

Nommage : tables au pluriel, PK `<Entity>Id`, dates de match en `date` (pas
`nvarchar(10)` — le tri ordinal sur string était une contrainte Firestore),
horodatages en `datetime2`, argent en `bigint` (dollars entiers, comme aujourd'hui).

### Référence NHL (global, hors ligue)

**`NhlTeams`** — `Abbrev` (PK, char(3)), Name, ConferenceName, DivisionName, LogoUrl

**`Players`** — `PlayerId` (PK, = id NHL, pas identity), FirstName, LastName, Position
(char(1)), PositionGroup (char(1), computed persisted), TeamAbbrev (FK), Status,
SweaterNumber, ShootsCatches, BirthDate, BirthCountry, HeightCm, WeightKg, HeadshotUrl,
DraftYear, DraftRound, DraftOverall, DraftTeamAbbrev, DraftChecked, LastSyncedUtc
→ index : (LastName, FirstName) pour la recherche, (TeamAbbrev), (Status)

**`PlayerContracts`** — `PlayerContractId`, PlayerId (FK), Season, CapHit, Aav,
TotalValue, YearsRemaining, ClauseType, Source (`capwages` | `csv` | `estimated`),
ImportedUtc → unique (PlayerId, Season)
*Remplace `Player.capHit`. La colonne actuelle avait une protection merge-field
manuelle pour ne pas être écrasée par `player-sync` — une table séparée règle ça
structurellement.*

**`Games`** — `GameId` (PK, = id NHL), Season (char(8)), GameType (tinyint),
GameDate (date), HomeTeamAbbrev, AwayTeamAbbrev, HomeScore, AwayScore, LastPeriodType,
SyncedUtc → index (GameDate), (Season, GameType, GameDate)

**`PlayerGameStats`** — PK composite (GameId, PlayerId). GameDate/Season/GameType
dénormalisés (pour un index couvrant sans jointure), TeamAbbrev, OpponentAbbrev,
Position, IsGoalie, IsHome, Toi, Pim, puis colonnes typées patineur (Goals, Assists,
Points, PlusMinus, Shots, Hits, BlockedShots, PowerPlayGoals) et gardien (ShotsAgainst,
Saves, GoalsAgainst, Decision, Starter, Shutout, OtLoss), SyncedUtc
→ index (GameDate) INCLUDE stats, (PlayerId, GameDate)

> **Colonnes typées, pas clé/valeur.** `StatLine`/`StatKeys` restent la représentation
> de *pointage* (une map, ce qui permet à un commissaire de scorer n'importe quelle
> stat sans changement de schéma) ; `StatLine.FromGameLine` reste l'adaptateur. Le
> stockage, lui, est typé — c'est tout l'intérêt de SQL.

**`NewsItems`** — Id, Source, Headline, Url, PlayerId (FK null), PlayerName,
PublishedUtc, FetchedUtc, ExternalKey (unique, upsert idempotent)

**`PlayerInjuries`** — PlayerId (FK), Status, InjuryType, ReportedUtc, ExpectedReturn,
Source *(modélisé, alimenté plus tard)*

### Calendrier

**`Periods`** — `PeriodId`, Season, Number, StartDate, EndDate, LockUtc, GameCount,
FinalizedUtc, CreatedUtc → unique (Season, Number). Global, append-only.

**`SimulationState`** — ligne unique (`Id = 1` avec CHECK), AsOfDate, Season, Enabled,
UpdatedUtc

### Pool

**`Users`** — `UserId`, Username (unique, normalisé), DisplayName, ExternalAuthId
(null), CreatedUtc, LastLoginUtc

**`Leagues`** — `LeagueId`, Name, Season, CommissionerUserId (FK), **`JoinCode`
(unique, court)**, CapAmount, RosterMin, RosterMax, ActiveForwards, ActiveDefense,
ActiveGoalies, CreatedUtc

> Le `JoinCode` est ce que l'API expose comme `id`. Aujourd'hui l'id du document
> Firestore sert de code d'invitation et le frontend le garde en `localStorage`.
> Exposer un code court plutôt qu'un entier garde `LeagueGate`/`Settings` inchangés.

**`LeagueMembers`** — PK (LeagueId, UserId), JoinedUtc → remplace le tableau
`memberUsernames`

**`LeagueScoringRules`** — PK (LeagueId, StatKey), PointValue (float) → remplace
`pointValues` + `extraPointValues`. L'API réassemble la même forme JSON, `RulesPanel.tsx`
ne bouge pas.

**`Teams`** — `TeamId`, LeagueId (FK), OwnerUserId (FK), Name, FranchiseAbbrev (FK
NhlTeams, null), CreatedUtc → unique (LeagueId, OwnerUserId)

> **Douze colonnes disparaissent** : `playerIds`, `playerPoints`, `playerNhlPoints`,
> `rosterGamesPlayed`, `capTotal`, `score`, `finalizedScore`, `periodPoints`,
> `benchScore`, `currentPeriodIndex`, `periodScores`, `finalizedThroughPeriodIndex`.
> Toutes des dénormalisations Firestore. C'est le plus gros nettoyage du chantier.

**`RosterSpots`** — `RosterSpotId`, LeagueId, TeamId (FK), PlayerId (FK),
PositionGroup (gelé à l'ouverture), StartDate, StartReason (tinyint), StartTradeId
(FK null), StartDraftPickId (FK null), EndDate (null), EndReason (tinyint null),
EndTradeId (FK null), OpenedUtc, ClosedUtc
→ index unique **filtré** `(LeagueId, PlayerId) WHERE EndDate IS NULL`
  ⇒ *« un joueur, un seul propriétaire par ligue » devient une contrainte de BD,
  plus un scan applicatif de toutes les équipes*
→ index (TeamId) WHERE EndDate IS NULL ; (LeagueId, StartDate, EndDate)

**`RosterAssignments`** — `RosterAssignmentId`, RosterSpotId (FK), PeriodId (FK),
IsActive, EffectiveFrom, EffectiveTo (la fenêtre réellement possédée, issue de
`StatWindow.Intersect`), les 14 stats agrégées de la période, **FantasyPoints**,
GamesPlayed, IsFinalized, ScoredUtc → unique (RosterSpotId, PeriodId)

> Remplace `Lineup` + `LineupResult`. **C'est ici que vit le banquage** : `IsFinalized`
> + `Period.FinalizedUtc`. Une ligne finalisée n'est jamais recalculée — un changement
> de barème ne réécrit pas le passé, comme aujourd'hui.

**`TeamPeriodLineups`** — PK (TeamId, PeriodId), SetBy (`auto` | username),
SubmittedUtc → porte l'info « alignement automatique » que l'UI affiche

**`DraftPicks`** — `DraftPickId`, LeagueId, Year, Round, PickInRound (null),
OriginalTeamId (FK), CurrentTeamId (FK), PlayerId (FK null), UsedUtc, CreatedUtc
→ unique (LeagueId, Year, Round, OriginalTeamId)

**`Trades`** — `TradeId`, LeagueId, ProposerTeamId, CounterpartyTeamId, Status
(tinyint), CreatedUtc, RespondedUtc, ProcessedUtc, EffectiveDate

**`TradeAssets`** — `TradeAssetId`, TradeId (FK), FromTeamId, ToTeamId, AssetType,
PlayerId (FK null), DraftPickId (FK null)
→ CHECK : exactement un des deux non-null, cohérent avec `AssetType`

> From/To **par actif** (plutôt que sur l'échange) rend un échange à trois équipes
> possible sans changement de schéma, sans compliquer le cas à deux.

**`TradeVotes`** — PK (TradeId, UserId), FavoredTeamId (FK null = « équitable »),
Magnitude, VotedUtc

### Vues

| Vue | Ce qu'elle donne |
|---|---|
| `vPlayerSeasonStats` | totaux saison par joueur (`GROUP BY` sur PlayerGameStats) |
| `vRosterSpotTotals` | points/GP par spot, actifs et banc séparés |
| `vTeamPeriodScores` | points actifs/banc par équipe par semaine → l'historique hebdo |
| `vStandings` | classement : SUM par équipe, cap total, GP roster, pts/match |

Les totaux *à une date* (mode test) sont des requêtes paramétrées, pas des vues.

---

## Structure des projets

```
FantasyWarrior.Core       ← inchangé sauf suppression des attributs [FirestoreData]
                            StatLine, StatKeys, StatWindow, PeriodScoring,
                            LineupRules, RuleConfig, TradeValidation, PoolClock,
                            NameNormalizer, PositionGroups  — reste pur, zéro EF
FantasyWarrior.Data       ← NOUVEAU : entités, FantasyWarriorDbContext,
                            IEntityTypeConfiguration<T> par entité, migrations,
                            classes de requêtes (ScoringQueries, StandingsQueries…)
FantasyWarrior.Api        ← mêmes routes, mêmes DTO ; DbContext au lieu de FirestoreDb
FantasyWarrior.Jobs       ← mêmes commandes CLI ; EF au lieu de Firestore
FantasyWarrior.Core.Tests ← les 152 tests existants, quasi inchangés
FantasyWarrior.Data.Tests ← NOUVEAU : tests d'intégration EF (Testcontainers SQL Server)
```

Paquets : `Microsoft.EntityFrameworkCore.SqlServer`, `.Design`, `.Tools`.
`EnableRetryOnFailure` **obligatoire** (le tier gratuit s'auto-suspend, la reprise
lève une transitoire) + `CommandTimeout` à 60s pour la première requête.
Migrations appliquées par une **commande explicite** (`dotnet run -- db-migrate`),
jamais au démarrage de l'API — Cloud Run scale à zéro et plusieurs instances qui
migrent en parallèle est un scénario à éviter.

---

## Le replug du UI : zéro changement

Vérifié dans `frontend/src/api.ts`, `App.tsx`, `Stats.tsx` : le frontend traite
`league.id` et `spotId` comme des **chaînes opaques**. Donc :

- `league.id` → le `JoinCode` (chaîne courte) — routes et `localStorage` inchangés
- `spotId` → `RosterSpotId.ToString()` — `Stats.tsx` s'en fiche
- les routes restent indexées par **username** (`/teams/{username}/lineup`), résolu
  en `TeamId` côté serveur

**Discipline : phase 1, les réponses JSON sont identiques au champ près.** Zéro
modification de `frontend/`. L'évolution du contrat (exposer les picks, les
contrats, les assignments historiques) vient après, quand le socle est vert.

Contrats à préserver exactement : `LeagueDetail` (dont `myRoster`, `teams[]`,
`currentPeriod`), `LineupDto` (dont `entries[]`, `used`, `slots`, `periods[]`),
`TeamSeasonStats.players[]` (dont les 5 champs `spot*`), `Trade` (dont la tally
`votes` en 5 buckets et `myVote`), `NewsArticle`, `RuleConfig`.

---

## Séquence d'exécution

**Phase 0 — Filet de sécurité et infra**
1. **Capturer les nombres de référence AVANT de toucher à quoi que ce soit** :
   dumper depuis Firestore, pour Les Mordus et la ligue de test, le `finalizedScore`
   et le `periodScores` par équipe et par semaine, dans un JSON versionné
   (`.claude/doc/golden-scores-preSql.json`). C'est le seul signal de correction
   sérieux de tout le chantier.
2. Chaîne de connexion Azure SQL (Nick fournit), secret GitHub `AZURE_SQL_CONNECTION`.
3. Pare-feu : les runners GitHub Actions ont des IP dynamiques → étape
   `az sql server firewall-rule create` en début de workflow (+ suppression en fin),
   plutôt que d'ouvrir 0.0.0.0.

**Phase 1 — Data project**
Entités + `FantasyWarriorDbContext` + configurations fluent + première migration.
Seed statique des 32 `NhlTeams`. `db-migrate` en commande de job.

**Phase 2 — Core dé-Firestorisé**
Retirer `[FirestoreData]`/`[FirestoreProperty]` de Core, casser la référence au
paquet `Google.Cloud.Firestore`. `RuleConfig` devient un POCO assemblé depuis
`LeagueScoringRules`. **Les 152 tests doivent rester verts** — c'est la porte de
sortie de cette phase.
`StatWindow` passe de `string` à `DateOnly` (le tri ordinal sur chaîne était une
contrainte Firestore ; les tests suivent mécaniquement).

**Phase 3 — Ingestion**
`player-sync`, `draft-sync`, `stats-sync`, `period-init`, `news-sync` sur EF.
Puis **ré-import de la saison 2025-26** : `stats-sync --from 2025-10-07 --to 2026-04-16`
(~1 342 appels boxscore, à lancer via GitHub Actions), puis `period-init --season 20252026`.
Vérification : 1 342 matchs, ~51 264 lignes, 28 semaines dont 2 mortes (olympiques,
9–22 février 2026).

**Phase 4 — Écritures pool**
`seed-mordus` (14 GM, 360 joueurs, 9F/4D/1G, 23–35 roster, 115 M$), `RosterChange`
sur EF (une transaction au lieu d'une séquence de writes), endpoints add/drop,
propositions/réponses d'échange, `process-trades`.

**Phase 5 — Pointage** *(le cœur)*
`PeriodRollupJob` réécrit : une requête `PlayerGameStats` sur la plage de la semaine
jointe aux `RosterSpots` chevauchant la période, une ligne `RosterAssignment` par
(spot, période). `NightlyJob` garde son ordre load-bearing (scorer → banquer →
échanges → matérialiser la semaine suivante). `recompute`, `period-lock`.
Validation de slots : transaction + requête de contrôle (strictement plus fort que
l'écriture atomique de champ unique d'aujourd'hui).

**Phase 6 — API**
Tous les endpoints, DTO identiques au champ près. `PlayerCache` disparaît (c'était
un cache de lectures Firestore).

**Phase 7 — Simulation**
`SimulationClock` sur la table `SimulationState`. `sim-reset`, `sim-advance`,
`sim-clock`. Le mode test devient nettement moins cher : plus de budget de lectures,
donc rejouer une saison complète d'un coup redevient possible.

**Phase 8 — Bascule**
Déploiement, retrait du code et des workflows Firestore, mise à jour de
`CLAUDE.md`, `deployment.md`, `project_status.md`, `scoring-model.md`, `testmode.md`.

---

## Vérification

1. **Tests unitaires** — les 152 existants verts après la phase 2, et à chaque phase.
2. **Tests d'intégration EF** (nouveau, `FantasyWarrior.Data.Tests` avec
   Testcontainers SQL Server) : les agrégats portent maintenant de la logique qui
   était en C# — les vues `vStandings`/`vRosterSpotTotals` et les requêtes bornées
   par date ont besoin de leur propre couverture. Conforme à la règle « toute
   feature qui touche la BD arrive avec ses tests de logique pure ».
3. **Comparaison aux nombres de référence** — après le rejeu de la saison en SQL,
   chaque score hebdomadaire par équipe doit correspondre **exactement** au JSON
   capturé en phase 0. Un écart = un bug, pas une approximation.
4. **Contrat d'API** — capturer les réponses JSON actuelles de chaque endpoint pour
   la ligue Les Mordus avant la bascule, rejouer contre l'API SQL, comparer champ
   par champ (les valeurs bougeront pour les données regénérées ; la **forme** ne
   doit pas bouger).
5. **Bout en bout** — `npm run dev` + API locale contre Azure SQL, parcourir les
   5 écrans, basculer un joueur actif/banc, proposer et accepter un échange,
   avancer la simulation d'une semaine.
6. **Budget Azure** — mesurer la consommation vCore-secondes d'un `nightly` et d'une
   semaine de `sim-advance` contre les 100 000/mois du tier gratuit.

---

## Risques identifiés

| Risque | Mitigation |
|---|---|
| **Auto-suspension du tier gratuit** (~30-60s de démarrage à froid sur la 1ʳᵉ requête) | `EnableRetryOnFailure`, timeout à 60s, et un ping de réchauffement si l'expérience est mauvaise |
| **Latence cross-cloud** si l'API reste sur Cloud Run | Data layer conçu sans N+1 : peu de requêtes, agrégats côté serveur. Décision d'hébergement reportée mais réversible |
| **100 000 vCore-secondes/mois** (~27 h de calcul) | À mesurer (point 6). Le rejeu de saison est l'opération la plus lourde |
| **IP dynamiques des runners GitHub Actions** vs pare-feu Azure | Règle de pare-feu créée/supprimée dans le workflow |
| **CapWages** : schéma exact des champs inconnu (doc publique incomplète) | Le job d'import est écrit contre un DTO isolé ; il faut la clé de Nick pour le valider en vrai |
| **Perte du temps réel Firestore** | Aucune perte réelle : rien ne l'utilise aujourd'hui (`firebase.ts` a été supprimé). Un futur temps réel = SignalR ou polling |

---

## Ce que ce chantier ne fait pas

Repêchage (mécanisme et écrans), agence libre, application réelle du plafond et des
tailles de roster, authentification, points du slot « Équipe », échanges à trois.
Tous sont **modélisés au schéma** — les construire ne demandera pas de migration.

---

## Amendements (après approbation)

### 1. CapWages : gratuit, et bien meilleur que prévu (2026-08-01)

Le plan supposait un abonnement payant. Nick a fourni un guide de scraping HTML
gratuit — et en regardant la page, la réalité est **meilleure que le guide** :

CapWages est un site **Next.js**. Chaque page embarque, dans un bloc
`<script id="__NEXT_DATA__">`, le JSON structuré à partir duquel son React a été
rendu — les mêmes chiffres que les tableaux visibles, déjà typés. On parse ça,
pas le HTML rendu. **Un changement de CSS ou de mise en page ne peut donc pas
casser l'import**, ce qui est la façon habituelle dont meurent les scrapers.

Deux conséquences qui changent la conception :

- **La page joueur porte `nhlId`.** Un contrat se joint directement à `Players`,
  sans aucun appariement par nom. Le guide n'avait pas repéré ça.
- **32 requêtes suffisent, pas ~1000.** Chaque page d'équipe porte le détail
  saison par saison de tout son roster. Les pages joueur ne servent qu'en
  repêchage pour les non-appariés (`--resolve-unmatched`), puisqu'elles seules
  ont `nhlId`.

Conditions respectées : 2 s entre requêtes, User-Agent honnête nommant le projet,
backoff exponentiel sur 429/503, usage personnel non commercial. `robots.txt`
vérifié le 2026-08-01 : `/players/` et `/trade-tree/` ne sont interdits qu'à
**Amazonbot**, autorisés pour tout autre agent.

**Livré et exécuté** — ce point est donc passé de « modéliser maintenant,
brancher plus tard » à **fait**.

### 2. Échanges : toutes les combinaisons (2026-08-01)

Nick a précisé qu'un échange peut contenir des joueurs **et** des choix, ensemble
ou séparément, dans toutes les combinaisons.

**Aucun changement nécessaire** — le schéma le fait déjà. `TradeAssets` a une
ligne par actif, un échange en a autant qu'il veut, et la contrainte `CHECK`
porte sur l'actif individuel (« un joueur ou un pick »), jamais sur l'échange.

### 3. Accès réseau Azure (2026-08-01)

Le serveur avait **`Deny Public Network Access = Yes`**, ce qui bloquait tout
(moi, Cloud Run, GitHub Actions) derrière un message trompeur : « Database is
not currently available ». Nick l'a passé à « Réseaux sélectionnés » + règle de
pare-feu sur son IP. À refaire pour toute nouvelle IP, et pour les runners
GitHub Actions au moment de brancher les workflows.

### 4. Le cold start du plan gratuit est réel et mesuré (2026-08-01)

Base en pause → première requête en échec pendant ~2 min, puis **10,4 s** pour
la reprise complète une fois réveillée. `EnableRetryOnFailure(6, 20s)` +
`CommandTimeout(60)` sont en place et suffisent pour les jobs. **À réévaluer
quand l'API sera branchée** : 10 s sur la première requête d'un utilisateur est
une mauvaise expérience, et c'est ce qui pourrait forcer la main sur la décision
d'hébergement (voir « Décisions prises »).
