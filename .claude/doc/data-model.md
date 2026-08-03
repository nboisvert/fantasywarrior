# Modèle de données — référence

> Le schéma Azure SQL et **pourquoi il est ce qu'il est**. Issu du chantier de
> migration Firestore → SQL (2026-08-02), conservé comme référence vivante.
> Les règles de pointage elles-mêmes vivent dans [scoring-model.md](scoring-model.md).

## Le principe directeur

**Un seul grain honnête : le `RosterAssignment`** — ce qu'un joueur a produit
pour une équipe pendant une semaine. Tout le reste est un `SUM`.

```
RosterAssignment.FantasyPoints     ← calculé une fois, gelé au banquage
  └─ SUM par spot    = ce que ce joueur a rapporté à cette équipe   (vue)
  └─ SUM par équipe  = le classement                                 (vue)
  └─ SUM par période = l'historique hebdomadaire                     (vue)
```

Aucune dérive possible, aucun job de synchronisation. Le modèle Firestore
stockait le même chiffre à trois grains et tenait l'invariant
`score = finalizedScore + periodPoints` à la main — c'est précisément ce qui
avait causé des bugs. Si le classement devenait lent (il ne l'est pas à ~10 k
lignes), l'échappatoire est une vue indexée, pas une colonne à maintenir.

Deux corollaires :

- **Les totaux de saison sont une vue, pas une table.** Un `GROUP BY` sur 51 k
  lignes avec index prend quelques millisecondes. La table de cache n'existait
  que pour économiser des lectures Firestore — et elle avait dû se doter d'un
  `throughDate` et de toute une logique d'invalidation pour servir le mode test.
  Un `WHERE GameDate <= @asOf` fait disparaître le problème *et* son code.
- **`Period` est une entité de premier plan**, pas un détail : la semaine
  lundi→dimanche sur la date de match, globale à toutes les ligues, immuable une
  fois écrite.

## Conventions

Tables au pluriel, PK `<Entity>Id`. Dates de match en `date` — le tri ordinal sur
chaîne était une contrainte Firestore. Horodatages en `datetime2`. Argent en
`bigint` (dollars entiers).

`EnableRetryOnFailure` est **obligatoire** : le plan gratuit s'auto-suspend et la
reprise lève une transitoire. Conséquence à connaître — une transaction manuelle
doit alors passer par `db.Database.CreateExecutionStrategy()`, sinon EF refuse.

Les migrations s'appliquent par commande explicite (`db-migrate`), jamais au
démarrage de l'API : plusieurs instances qui migrent en parallèle est un scénario
à éviter.

---

## Référence NHL (globale, hors ligue)

**`NhlTeams`** — `Abbrev` (PK, char(3)), Name, ConferenceName, DivisionName, LogoUrl

**`Players`** — `PlayerId` (PK, = id NHL, pas identity), FirstName, LastName,
Position (char(1)), PositionGroup (char(1), calculée persistée), TeamAbbrev (FK),
Status, SweaterNumber, ShootsCatches, BirthDate, BirthCountry, HeightCm, WeightKg,
HeadshotUrl, DraftYear, DraftRound, DraftOverall, DraftTeamAbbrev, DraftChecked,
CapWagesSlug, CareerStatsSyncedUtc, LastSyncedUtc
→ index : (LastName, FirstName) pour la recherche, (TeamAbbrev), (Status)

**`PlayerContracts`** — `PlayerContractId`, PlayerId (FK), Season, CapHit, Aav,
TotalValue, YearsRemaining, ClauseType, Source, ImportedUtc → unique (PlayerId, Season)

> Une table plutôt qu'une colonne `Player.CapHit` : les contrats changent chaque
> année, et l'ancienne colonne avait besoin d'une protection merge-field écrite à
> la main pour survivre à un `player-sync`. Une table sépare structurellement.
>
> **Toujours filtrer par saison.** Les contrats courent des années à l'avance ;
> prendre le plus récent fait mentir l'affichage (Jack Eichel : 10 M$ en 2025-26,
> 13,5 M$ dès 2026-27).

**`Games`** — `GameId` (PK, = id NHL), Season (char(8)), GameType (tinyint),
GameDate (date), HomeTeamAbbrev, AwayTeamAbbrev, HomeScore, AwayScore,
LastPeriodType, SyncedUtc → index (GameDate), (Season, GameType, GameDate)

**`PlayerGameStats`** — PK composite (GameId, PlayerId). GameDate/Season/GameType
dénormalisés (pour un index couvrant sans jointure), TeamAbbrev, OpponentAbbrev,
Position, IsGoalie, IsHome, Toi, Pim, puis colonnes typées patineur (Goals,
Assists, Points, PlusMinus, Shots, Hits, BlockedShots, PowerPlayGoals) et gardien
(ShotsAgainst, Saves, GoalsAgainst, Decision, Starter, Shutout, OtLoss), SyncedUtc
→ index (GameDate) INCLUDE stats, (PlayerId, GameDate)

**`PlayerCareerSeasonStats`** — `PlayerCareerSeasonStatId` (PK, surrogate),
PlayerId (FK), Season (char(8)), GameType (regular season only pour l'instant),
LeagueAbbrev, TeamName, GamesPlayed, puis colonnes patineur (Goals, Assists,
Points, Pim, PlusMinus) et gardien (Wins, Losses, OtLosses, GoalsAgainst,
GoalsAgainstAvg, SavePctg, Shutouts) → unique (PlayerId, Season, GameType,
LeagueAbbrev, TeamName)

> Carrière complète (junior, NCAA, Europe, AHL, NHL) tirée du player-landing
> de l'API NHL, filtrée par [`NotableLeagues`](../../backend/FantasyWarrior.Core/Players/NotableLeagues.cs)
> (liste blanche des ligues majeures — l'API renvoie aussi des tournois pee-wee).
> Alimentée par `career-sync`, qui rafraîchit les joueurs les plus périmés
> (`Player.CareerStatsSyncedUtc`) plutôt qu'une synchro unique comme
> `DraftChecked` : la ligne de la saison en cours change toute l'année.
> Manuel pour l'instant, pas encore dans le cron nightly.

> **Colonnes typées, pas clé/valeur.** `StatLine`/`StatKeys` restent la
> représentation de *pointage* — une map, ce qui permet à un commissaire de scorer
> n'importe quelle statistique sans changement de schéma — et
> `StatLine.FromGameLine` reste l'adaptateur. Le stockage, lui, est typé : c'est
> tout l'intérêt de SQL.

**`NewsItems`** — Id, Source, Headline, Url, PlayerId (FK null), PlayerName,
PublishedUtc, FetchedUtc, ExternalKey (unique, upsert idempotent)

**`PlayerInjuries`** — PlayerId (FK), Status, InjuryType, ReportedUtc,
ExpectedReturn, Source *(modélisée, pas encore alimentée)*

## Calendrier

**`Periods`** — `PeriodId`, Season, Number, StartDate, EndDate, LockUtc, GameCount,
FinalizedUtc, CreatedUtc → unique (Season, Number). Globale, append-only.

**`SimulationState`** — ligne unique (`Id = 1` avec CHECK), AsOfDate, Season,
Enabled, UpdatedUtc

## Pool

**`Users`** — `UserId`, Username (unique, normalisé), DisplayName,
ExternalAuthId (null, prévu pour l'auth), CreatedUtc, LastLoginUtc

**`Leagues`** — `LeagueId`, Name, Season, CommissionerUserId (FK), **`JoinCode`
(unique, court)**, CapAmount, RosterMin, RosterMax, ActiveForwards, ActiveDefense,
ActiveGoalies, CreatedUtc

> Le `JoinCode` est ce que l'API expose comme `id`. Le frontend traite `league.id`
> comme une chaîne opaque et le garde en `localStorage` — exposer un code court
> plutôt qu'un entier a permis de ne pas toucher `LeagueGate`/`Settings`.

**`LeagueMembers`** — PK (LeagueId, UserId), JoinedUtc

**`LeagueScoringRules`** — PK (LeagueId, StatKey), PointValue (float). L'API
réassemble la même forme JSON qu'avant, donc `RulesPanel.tsx` n'a pas bougé.

**`Teams`** — `TeamId`, LeagueId (FK), OwnerUserId (FK), Name, FranchiseAbbrev
(FK NhlTeams, null), CreatedUtc → unique (LeagueId, OwnerUserId)

> **`Teams` ne porte aucune colonne de score.** Douze dénormalisations Firestore
> ont disparu ici : `playerIds`, `playerPoints`, `playerNhlPoints`,
> `rosterGamesPlayed`, `capTotal`, `score`, `finalizedScore`, `periodPoints`,
> `benchScore`, `currentPeriodIndex`, `periodScores`,
> `finalizedThroughPeriodIndex`. C'est le plus gros nettoyage du chantier.

**`RosterSpots`** — `RosterSpotId`, LeagueId, TeamId (FK), PlayerId (FK),
PositionGroup (gelé à l'ouverture), StartDate, StartReason (tinyint), StartTradeId
(FK null), StartDraftPickId (FK null), EndDate (null), EndReason (tinyint null),
EndTradeId (FK null), OpenedUtc, ClosedUtc

→ index unique **filtré** `(LeagueId, PlayerId) WHERE EndDate IS NULL`
→ index (TeamId) WHERE EndDate IS NULL ; (LeagueId, StartDate, EndDate)

> Cet index filtré fait de « un joueur, un seul propriétaire par ligue » une
> **contrainte de base de données**, là où Firestore exigeait un scan applicatif
> de toutes les équipes. Le contrôle applicatif subsiste, mais seulement pour
> produire un message lisible — ce n'est plus lui qui garantit quoi que ce soit.
>
> Un spot est **fermé, jamais supprimé** : l'équipe garde définitivement ce que
> ce joueur lui a banqué.

**`RosterAssignments`** — `RosterAssignmentId`, RosterSpotId (FK), PeriodId (FK),
IsActive, EffectiveFrom, EffectiveTo (la fenêtre réellement possédée, issue de
`StatWindow.Intersect`), les 14 statistiques agrégées de la période,
**FantasyPoints**, GamesPlayed, IsFinalized, ScoredUtc → unique (RosterSpotId, PeriodId)

> **C'est ici que vit le banquage** : `IsFinalized` + `Period.FinalizedUtc`. Une
> ligne finalisée n'est jamais recalculée — un changement de barème ne réécrit
> pas le passé.

**`TeamPeriodLineups`** — PK (TeamId, PeriodId), SetBy (`auto` | username),
SubmittedUtc → porte l'information « alignement automatique » qu'affiche l'UI

**`DraftPicks`** — `DraftPickId`, LeagueId, Year, Round, PickInRound (null),
OriginalTeamId (FK), CurrentTeamId (FK), PlayerId (FK null), UsedUtc, CreatedUtc
→ unique (LeagueId, Year, Round, OriginalTeamId)

> Propriétaire distinct de l'origine : ça donne gratuitement l'affichage
> « 2e ronde de PIT via BOS ».

**`Trades`** — `TradeId`, LeagueId, ProposerTeamId, CounterpartyTeamId, Status
(tinyint), CreatedUtc, RespondedUtc, ProcessedUtc, EffectiveDate

**`TradeAssets`** — `TradeAssetId`, TradeId (FK), FromTeamId, ToTeamId, AssetType,
PlayerId (FK null), DraftPickId (FK null)
→ CHECK : exactement un des deux non-null, cohérent avec `AssetType`

> Une ligne par actif, et From/To **par actif** plutôt que sur l'échange. Ça rend
> un échange à trois équipes possible sans changement de schéma, sans compliquer
> le cas à deux. Ça couvre aussi d'office « joueurs et choix, toutes les
> combinaisons » : la contrainte porte sur l'actif individuel, jamais sur
> l'échange.

**`TradeVotes`** — PK (TradeId, UserId), FavoredTeamId (FK null = « équitable »),
Magnitude, VotedUtc

**`Messages`** — `MessageId` (bigint), LeagueId (FK), SenderUserId (FK),
RecipientUserId (FK), Body (nvarchar 1000), SentUtc, ReadUtc (null)
→ index (LeagueId, SenderUserId, RecipientUserId, SentUtc) pour lire un fil
→ index filtré `IX_Messages_Unread` sur (RecipientUserId, LeagueId) `WHERE ReadUtc IS NULL`

> **Les fils sont par ligue.** Un usager appartient à plusieurs pools ; les mêmes
> deux personnes qui se parlent dans deux ligues ont deux conversations, parce
> que le contexte est le pool. Ça garde aussi la liste de contacts trivialement
> juste : c'est la membriété de la ligue, jamais une union entre pools.
>
> **Pas de table `Conversations`.** Un fil, c'est « les messages entre ces deux
> usagers », lu dans les deux sens ; à une douzaine de GMs, la jointure que ça
> économiserait ne vaut pas la ligne qu'elle coûterait. Le regroupement vit dans
> `ConversationSummary` (Core), donc il est testé sans base.
>
> **`ReadUtc` null = non lu**, et c'est toute la requête du badge — aucun
> compteur à tenir en accord avec les lignes. L'index filtré reste de la taille
> de ce qui est effectivement non lu plutôt que de l'historique, qui lui ne fait
> que grossir.
>
> ⚠️ **Les deux FK vers `Users` sont en NO ACTION**, et pas seulement par
> l'habitude conservatrice du reste du schéma : deux chemins de cascade vers la
> même table, c'est l'erreur « may cause cycles or multiple cascade paths » et
> SQL Server refuse carrément de créer la contrainte.

**`Users.LastSeenUtc`** — estampillé par le middleware de présence de l'API sur
le trafic ordinaire, pas seulement au login.

> Sert **uniquement à formuler le libellé** (« last seen 45min ago ») pour ceux
> qui ne sont pas en ligne. La pastille verte, elle, ne dépend que du registre
> de connexions SignalR en mémoire : en ligne == une connexion vivante, point.
> Il y a eu une fenêtre de grâce de 90 s ; elle obligeait à retarder l'annonce
> hors-ligne au-delà d'elle-même, donc à un timer détaché, qui finissait par la
> contredire. Un seul prédicat supprime toute cette classe de bug.

## Vues

| Vue | Ce qu'elle donne |
|---|---|
| `vPlayerSeasonStats` | totaux saison par joueur (`GROUP BY` sur `PlayerGameStats`) |
| `vRosterSpotTotals` | points et matchs par spot, actifs et banc séparés |
| `vTeamPeriodScores` | points actifs/banc par équipe par semaine → l'historique hebdomadaire |
| `vStandings` | classement : SUM par équipe, cap total, matchs du roster, points/match |

Les totaux **à une date** (mode test) sont des requêtes paramétrées, pas des
vues — c'est la même agrégation avec un `WHERE GameDate <= @asOf`.

---

## CapWages — la source des contrats

CapWages est un site **Next.js**. Chaque page embarque dans un bloc
`<script id="__NEXT_DATA__">` le JSON structuré à partir duquel son React a été
rendu : les mêmes chiffres que les tableaux visibles, déjà typés. **On parse ce
JSON, pas le HTML rendu** — un changement de CSS ou de mise en page ne peut donc
pas casser l'import, ce qui est la façon habituelle dont meurent les scrapers.

- **La page joueur porte `nhlId`** : un contrat se joint directement à `Players`,
  sans aucun appariement par nom.
- **32 requêtes suffisent, pas ~1 000** : chaque page d'équipe porte le détail
  saison par saison de tout son roster. Les pages joueur ne servent qu'en
  repêchage pour les non-appariés (`--resolve-unmatched`), puisqu'elles seules
  portent `nhlId`.

Conditions respectées : 2 s entre requêtes, User-Agent honnête nommant le projet,
backoff exponentiel sur 429/503, **usage personnel non commercial**. `robots.txt`
vérifié le 2026-08-01 : `/players/` et `/trade-tree/` ne sont interdits qu'à
Amazonbot.

---

## La validation contre l'oracle

Le résultat le plus important du chantier, conservé parce qu'il dit ce en quoi on
peut avoir confiance.

En rejouant les semaines 1 et 2 contre un instantané pré-migration
(`golden-scores-preSql.json`), joueur par joueur :

- **Les alignements sont identiques.** Les deux systèmes choisissent exactement
  les mêmes 14 joueurs. Auto-remplissage, fenêtre de statistiques, banquage et
  vues reproduisent l'ancien moteur à l'identique.
- **Un seul écart par joueur, et c'est l'ancien système qui avait tort.** Sergei
  Bobrovsky, semaine 1 : 3 matchs, 3 victoires. À 2 pts la victoire, SQL calcule
  6 ; Firestore avait `gamesPlayed=3, points=3` — ses champs de décision de
  gardien n'étaient pas correctement captés.
- Cette seule classe d'erreur explique **tous** les écarts restants : ils vont
  tous dans le même sens (SQL ≥ Firestore), et les plus gros appartiennent aux
  équipes qui alignent le plus de départs de gardien.

**Conséquence** : l'écart de 1 265 lignes (49 999 contre 51 264) n'était pas une
perte côté SQL — l'ancien journal de match était partiellement faux. La règle est
désormais épinglée par
`StatColumnsTests.ThreeGoalieWinsScoreSixUnderTheMordusScale`.

⚠️ **Pour refaire cette comparaison**, semer avec `--no-opening-lineup` :
l'ancien système n'utilisait jamais la liste `Active` du PDF, il auto-remplissait.
Les deux produisent des scores légitimes mais différents, et l'oracle ne valide
le moteur que si les *entrées* concordent.

## Pièges trouvés en route

Cinq bugs silencieux, tous corrigés, tous instructifs :

1. **Une semaine banquée sans avoir été scorée.** L'étape de pointage ne traite
   que la semaine *en cours*, qui au moment où une semaine devient banquable est
   déjà la *suivante*. Les semaines gelaient sur des chiffres partiels — zéro
   dans un rejeu. Le banquage rescore une dernière fois avant de geler, ce qui
   donne aussi son sens au jour de grâce.
2. **`wipe-pools` laissait les périodes marquées « banquées ».** Les frontières
   de semaine sont du calendrier et survivent ; « banquée » est de l'état de pool
   et ne doit pas. Une ligue fraîche ne pouvait plus jamais banquer ces semaines.
3. **L'auto-remplissage ne classait rien** — `SeasonPointsToDate` valait 0 pour
   tous les candidats, donc « les meilleurs disponibles » signifiait en fait
   « les plus petits ids ».
4. **`season-stats` ignorait le curseur de simulation** alors que la carte joueur
   le respectait : le même joueur affichait 74 matchs d'un côté et 0 de l'autre,
   sur le même écran.
5. **`.gitignore` ne protégeait pas `appsettings.Local.json`** — le motif
   `appsettings.*.Local.json` exige un segment au milieu. **Le dépôt est public**,
   donc un identifiant commité y serait lisible pour toujours.

## Hors périmètre, mais déjà au schéma

Repêchage, agence libre, application réelle du plafond et des tailles de roster,
authentification, points du slot « Équipe », échanges à trois équipes. Les
construire ne demandera **aucune migration**.
