# Data model — reference

> The Azure SQL schema and **why it is shaped the way it is**. Scoring rules:
> [scoring-model.md](scoring-model.md). Off-season behaviour:
> [offseason.md](offseason.md). External sources: [integrations.md](integrations.md).

## The guiding principle

**One honest grain: the `RosterAssignment`** — what a player produced for a team
in one week. Everything else is a `SUM` served by a view: per spot (what he earned
this team), per team (the standings), per period (the weekly history).

Storing the same number at three grains means holding an invariant by hand, which
is where bugs come from. Nothing here can drift and there is no synchronisation
job. If the standings ever became slow (they are not, at ~10k rows) the escape
hatch is an indexed view, never a column to maintain. So **season totals are a
view, not a table** — a `GROUP BY` over 51k indexed rows takes milliseconds, and
totals *as of a day* are the same aggregation with `WHERE GameDate <= @asOf`, one
parameter instead of a cache table, its `throughDate` and its invalidation logic.
And **`Period` is a first-class entity**: the Monday→Sunday week on game date,
global across leagues, immutable once written.

## Conventions

Tables plural, PK `<Entity>Id`. Game dates `date`, timestamps `datetime2`, money
`bigint` (whole dollars). Codes (abbreviations, seasons, stat keys) fixed-length
non-Unicode; free text people type is Unicode.

⚠️ `EnableRetryOnFailure` is **mandatory** (`DataServiceCollectionExtensions.cs:113`):
the free tier auto-suspends and waking throws a transient. Consequence — a manual
transaction must then go through `db.Database.CreateExecutionStrategy()`, or EF
refuses it outright.

Migrations apply by explicit command (`db-migrate`), never at API startup: several
instances migrating in parallel is a scenario to avoid. See
[deployment.md](deployment.md).

---

## NHL reference (global, outside any league)

**`NhlTeams`** — `Abbrev` (PK, char(3)), Name, ConferenceName, DivisionName,
LogoUrl. Seeded; everything else references it, so it must exist first.

**`Players`** — `PlayerId` (PK, = NHL id, never generated), FirstName, LastName,
Position (char(1)), PositionGroup (char(1), computed by the database from
`Position` and stored, so it cannot disagree and can still be indexed), TeamAbbrev
(FK), Status, SweaterNumber, ShootsCatches, BirthDate, BirthCountry, HeightCm,
WeightKg, HeadshotUrl, DraftYear, DraftRound, DraftOverall, DraftTeamAbbrev,
DraftChecked, CapWagesSlug, CareerStatsSyncedUtc, CareerNhlGames (null),
LastSyncedUtc
→ index (LastName, FirstName) for search, (TeamAbbrev), (Status), (DraftChecked)
filtered `= 0` — `draft-sync`'s backfill scan, which matches almost nothing once
done — and (CareerStatsSyncedUtc) unfiltered, because `career-sync` rolls forever.

> **`CareerNhlGames`** — career NHL regular-season games, the sum of the
> `PlayerCareerSeasonStats` rows with `LeagueAbbrev = 'NHL'`. **Null exactly when
> `CareerStatsSyncedUtc` is**: zero games and "never looked" are different states,
> and a veteran whose sync failed must not read as a rookie. Stored rather than
> summed on read because it has one writer (`career-sync`, in the same
> `SaveChanges` as the rows it derives from) and a column compares in SQL.
> [`ProtectionRules.IsAutoProtected`](../../backend/FantasyWarrior.Core/Drafts/ProtectionRules.cs)
> reads it: too few NHL games and nobody may draft the player away from his GM.
> **Store the measurement, derive the verdict** — moving a threshold rewrites no
> rows. Stale by at most `career-sync`'s window, a reason for the draft itself to
> freeze the number rather than read it live.

**`PlayerContracts`** — `PlayerContractId`, PlayerId (FK cascade), Season, CapHit,
Aav, TotalValue, YearsRemaining, ClauseType, Source, ImportedUtc → unique
(PlayerId, Season), so a re-import updates instead of accumulating. A table rather
than a `Player.CapHit` column: contracts change every year, and a column needs a
hand-written merge-field guard to survive `player-sync`. **Always filter by
season** — contracts run years ahead, so taking the most recent makes the display
lie (Eichel: $10M in 2025-26, $13.5M from 2026-27).

**`Games`** — `GameId` (PK, = NHL id), Season (char(8)), GameType (tinyint),
GameDate (date), HomeTeamAbbrev, AwayTeamAbbrev, HomeScore, AwayScore,
LastPeriodType, SyncedUtc → index (GameDate), (Season, GameType, GameDate) — the
second is how `period-init` derives the weekly calendar.

**`PlayerGameStats`** — composite PK (GameId, PlayerId); no surrogate key earns its
keep. GameDate/Season/GameType denormalised (covering index, no join), TeamAbbrev,
OpponentAbbrev, Position, IsGoalie, IsHome, Toi, Pim, typed skater columns (Goals,
Assists, Points, PlusMinus, Shots, Hits, BlockedShots, PowerPlayGoals) and goalie
columns (ShotsAgainst, Saves, GoalsAgainst, Decision, Starter, Shutout, OtLoss),
SyncedUtc
→ index (GameDate) INCLUDE the stats — THE query, one scoring week for every
league at once, covering so the rollup never touches the base table — plus
(PlayerId, GameDate) for the player card.

> **Typed columns, not key/value.** `StatLine`/`StatKeys` remain the *scoring*
> representation — a map, which is what lets a commissioner score any statistic
> without a schema change — and `StatLine.FromGameLine` is the adapter. Storage is
> typed: that is the point of SQL.
>
> ⚠️ `Player` is NO ACTION here: `Games` already cascades into this table, and a
> second cascade path into the same table is rejected by SQL Server.

**`PlayerCareerSeasonStats`** — `PlayerCareerSeasonStatId` (PK), PlayerId (FK, NO
ACTION), Season, GameType (regular season only for now), LeagueAbbrev, TeamName,
GamesPlayed, skater columns (Goals, Assists, Points, Pim, PlusMinus) and goalie
columns (Wins, Losses, OtLosses, GoalsAgainst, GoalsAgainstAvg, SavePctg, Shutouts)
→ unique (PlayerId, Season, GameType, LeagueAbbrev, TeamName) — a mid-season trade
legitimately gives two rows, and `career-sync`'s full-replace upsert relies on it;
index (PlayerId, Season) for the Career tab. Full career (junior, NCAA, Europe,
AHL, NHL) from the NHL API's player-landing, filtered by
[`NotableLeagues`](../../backend/FantasyWarrior.Core/Players/NotableLeagues.cs) —
a whitelist, because the API also returns pee-wee tournaments.

> **The notion of a prospect is read here and nowhere else.** A player is a
> prospect while he has **no career NHL game**: no `LeagueAbbrev = 'NHL'` row with
> `GamesPlayed > 0`. He stops being one the day he debuts, without anyone saying
> so. **Not `Players.Status`**, which carries the value `"prospect"` but means "not
> on an NHL club's roster" — a different set, so reading it here would silently
> answer another question. **Derived, never stored**: a column would be maintained
> by `career-sync`, which is not in the nightly cron, so it would be stale on
> exactly the day that matters. The computation is
> [`Prospects.ForAsync`](../../backend/FantasyWarrior.Data/Players/Prospects.cs);
> a player `career-sync` has never reached (`CareerStatsSyncedUtc` null) is **not**
> reported as a prospect — the same distinction as `DraftChecked`.
>
> **Under a replay the rule ignores the simulated date**: career rows are per
> season, not per date, so a player who debuted in March 2026 counts as having
> played even with the cursor in December 2025. Fixable by bounding the current
> season through `PlayerGameStats` × `Games.GameDate`, which are dated.

**`NewsItems`** — `NewsItemId`, Source, Headline, **Body**, Url, PlayerId (FK null,
SET NULL), PlayerName, PublishedUtc, FetchedUtc, ExternalKey → unique (Source,
ExternalKey), making the nightly sync an idempotent upsert; index (PublishedUtc
DESC). `Body` is the factual paragraph under the headline — what makes the player
card's News tab useful rather than decorative. **Never** Rotowire's
subscription-locked "ANALYSIS" block.

**`PlayerInjuries`** — `PlayerInjuryId`, PlayerId (FK cascade), Status, InjuryType,
ReportedUtc, ExpectedReturn (never populated), Source, ResolvedUtc → index on
PlayerId filtered `WHERE ResolvedUtc IS NULL`, this table's only query.

> One open row per player **and per source** — the grain matters, because the two
> sources are reconciled independently. `Status` carries the **kind** of
> unavailability (`Injured` / `Suspended`), not a severity: neither source
> publishes "Out / IR / Day-to-day", and `InjuryClassifier` is its only author.
> `ExpectedReturn` stays empty — the return is stated only in prose, and guessing
> would be worse than showing nothing. What opens and closes these rows is
> [integrations.md](integrations.md).

## Calendar

**`Seasons`** — `Season` (PK, char(8)), RegularSeasonStart, RegularSeasonEnd,
PlayoffStart (null), PlayoffEnd (null), ScheduleImportedUtc (null). Written by
`season-init` from the schedule the NHL publishes; `20252026` is seeded by the
migration that creates the table.

> **The season string stays the key; the table carries what the string cannot.**
> `"20262027"` is the NHL's own identifier and is already the join value on
> `Games`, `PlayerGameStats`, `PlayerContracts`, `PlayerCareerSeasonStats`,
> `Periods`, `SimulationState`, `Leagues` and `LeagueSeasons`, and succession
> arithmetic is a pure function (`Core/Seasons/Season.cs`). What it cannot carry
> is **dates** — and dates are what let a calendar exist before its games do.
>
> **Nothing has a foreign key to it.** A constraint on 51k `PlayerGameStats`
> rows would guarantee only what the string already does, and would force an
> insert order on every sync job. The link is a value the application keeps
> honest, the same posture as `Leagues.Season` → `LeagueSeasons`.
>
> **Declared, not observed.** These dates are the published schedule, which is
> why they exist months before a game does. `Games` say what actually happened.
> `SeasonBounds.Resolve` takes the **union** of the two — a rescheduled game
> outside every period would score for nobody, and stopping at the last game
> played would leave the rest of the season with no weeks at all.
> `ScheduleImportedUtc`, stamped by `stats-sync`, is the one thing separating
> "no games that week" from "no games imported yet".

**`Periods`** — `PeriodId`, Season, Number, StartDate, EndDate, LockUtc, GameCount,
FinalizedUtc, CreatedUtc → unique (Season, Number); index (Season, StartDate) =
"which week is it". Global, append-only, never deleted: deleting one would restate
points teams already own.

> **`Periods` deliberately has no FK to `LeagueSeasons`.** A week is a property of
> the NHL calendar, not of a pool — which is exactly what lets the nightly job
> fetch a week's game rows **once, by date range, for every league at the same
> time**. `Periods` carries the NHL season string and `LeagueSeasons` references
> the same string: two different grains, correctly kept apart.

**`SimulationState`** — single row (`SimulationStateId = 1`, CHECK-enforced),
AsOfDate, Season, Enabled, UpdatedUtc. The simulated instant derives as the *next*
day, exactly the real relation between "today" and "the last day with results",
which is why no scoring code has a simulation special case. See
[testmode.md](testmode.md).

## Pool

**`Users`** — `UserId`, Username (unique, trimmed and lowercased), DisplayName,
ExternalAuthId (null, unique filtered, reserved for real auth), CreatedUtc,
LastLoginUtc, LastSeenUtc

> **`LastSeenUtc` is stamped by the viewer, never the viewed.** The presence
> middleware resolves who that is through
> [`PresenceStamping.ResolveViewer`](../../backend/FantasyWarrior.Core/Messaging/PresenceStamping.cs):
> **only the query string names the viewer** (`viewer` first, then `username`). A
> route segment counts only in the `/api/users/{username}/…` family, where the
> subject is the caller by construction — on a league-scoped team route that
> segment names the team's *owner*, so trusting it would stamp a rival as active
> whenever someone opened his roster. Everywhere else an ambiguous request stamps
> nobody: a missed stamp costs a stale label, a wrong one invents activity that
> never happened. It serves **only to word the label** ("last seen 45min ago") for
> people who are not online: the green dot depends solely on the in-memory SignalR
> connection registry, online == a live connection, full stop. A grace window
> forces the offline announcement to be delayed past itself, hence a detached timer
> that ends up contradicting it; one predicate removes the whole class of bug.
>
> **Both timestamps are readable, commissioner-only**, via
> `GET /api/leagues/{leagueId}/activity`, which returns them per member:
> `lastLoginUtc` is a deliberate act, `lastSeenUtc` is any traffic at all. It
> deliberately has no screen — a per-GM last-login list on a public route is a
> surveillance feature nobody asked for.

**`Leagues`** — `LeagueId`, Name, Season, CommissionerUserId (FK), **`JoinCode`
(unique, short)**, CapAmount, DefaultCapHit, RosterMin, RosterMax, ProtectionSlots
(null), StealRounds (null), MaxLossesPerTeam (null), DraftRounds (null),
ActiveForwards, ActiveDefense, ActiveGoalies, CreatedUtc. Les Mordus' own values:
[mordus.md](mordus.md). The `JoinCode` is what the API exposes as `id`; the
frontend treats `league.id` as an opaque string and keeps it in `localStorage`, so
it is looked up on essentially every request.

> **`DefaultCapHit`** — what a player with no contract on file costs against the
> cap. "No contract" is not a data gap to wait out: it is a real, permanent state
> for an unsigned free agent and for a drafted prospect, and a keeper pool holds
> plenty of both. Not nullable — every league needs an answer, and null would only
> be "$0" spelled at greater length. Set it to 0 to count them as free.

> **`ProtectionSlots`** — how many roster spots a GM may protect before the steal
> draft; a player who qualifies for `IsAutoProtected` does not spend one.
> **`StealRounds`** sizes the steal segment **without generating a single row**: a
> steal turn is not tradable, so every team has exactly that many and there is
> nothing to own. **`MaxLossesPerTeam`** caps what one team may lose across a whole
> steal draft, closing the pool from underneath while the draft runs — which is why
> the available list is recomputed each turn and never cached. On all three,
> **null means the league does not have that rule, not zero.**
>
> **`StealRounds` is deliberately independent of `DraftRounds`**: they size two
> different drafts that run back to back, and seeing them diverge is not an error.
> `DraftRounds` generates one `DraftPicks` row per team per round.

**`LeagueSeasons`** — `LeagueSeasonId`, LeagueId (FK cascade), Season, Number,
Phase (tinyint: Preparing/Protecting/Drafting/PreSeason/InSeason/Complete),
**`Rules` (nvarchar(max), the rules document)**, ChampionTeamId (FK Teams, null),
StartedUtc, CompletedUtc
→ unique (LeagueId, Season); unique (LeagueId, Number)
→ unique **filtered** `(LeagueId) WHERE Phase <> 5` — at most one non-`Complete`
row per league, a real constraint rather than a sentence in a doc

> The table half of "season": `Leagues.Season` names the NHL season whose points
> count now; this is the league's **own** count ("season 3"), the home of
> `Phase`, and the home of **every rule the league plays by**.
>
> **`Rules` is one JSON document** — the whole `RuleSet` (`Core/Rules/`), mapped
> through a value converter rather than EF's owned-JSON support, because the
> scale and its per-position overrides are dictionaries keyed by data. **The
> `ValueComparer` is mandatory**: `RuleSet` is a mutable graph, so EF's default
> reference equality would compare a tracked entity to itself, conclude nothing
> changed, and drop every rules edit at `SaveChanges` with no error anywhere.
>
> **Rules live here, not on `Leagues`, because rules have a season.** Mutating
> them in place left a keeper pool unable to answer "what were season 2's
> rules?"; one document per season makes that history a consequence of where the
> rules are stored. It also means **two documents are live through the whole
> off-season** — the season being scored (`Leagues.Season`, sitting `Complete`)
> and the season being prepared — which is why every read goes through
> `RuleSetResolver`, whose three entry points *are* the three questions.
>
> **The column defaults to `'{}'`, which reads as `IsUnwritten`, not as the
> defaults.** A league whose rules were never converted would otherwise report
> "no cap, no slots, no protections" and look exactly like a correctly configured
> permissive league. Every reader refuses instead and names `rules-backfill`.
>
> ⚠️ **`Leagues.Season` has no composite FK to this table, and cannot have one.** A
> composite FK on (LeagueId, Season) refuses the very first insert: creating a
> league inserts the `Leagues` row **first**, because that row is what hands out
> the `LeagueId` any `LeagueSeasons` row would need to reference — and SQL Server
> has no deferred-constraint escape hatch. The link stays a value the application
> keeps honest, exactly as `Team.FranchiseAbbrev` → `NhlTeam.Abbrev` already does.

**`LeagueMembers`** — PK (LeagueId, UserId), JoinedUtc → index (UserId), which is
"my leagues", the first query every session makes.

**`LeagueScoringRules`** — PK (LeagueId, StatKey), PointValue (float). One row per
stat rather than columns: that is what lets a commissioner score blocked shots or
hits without a schema change.

**`Teams`** — `TeamId`, LeagueId (FK cascade), OwnerUserId (FK), Name,
FranchiseAbbrev (FK NhlTeams, null), CreatedUtc → unique (LeagueId, OwnerUserId)

> `Teams.FranchiseAbbrev` is the team's **identity** in the pool and never moves.
> The franchise it **owns** is a `RosterSpot` of group `T`, and that is the one a
> trade moves. The two start out equal and must be able to diverge — the club you
> are is not the club you own. **`Teams` carries no score column at all**: every
> total is a SUM over `RosterAssignments`, so nothing here can drift.

**`RosterSpots`** — `RosterSpotId`, LeagueId (FK, NO ACTION), TeamId (FK cascade),
**PlayerId (FK null)**, **FranchiseAbbrev (FK NhlTeams, null)**, PositionGroup
(`F`/`D`/`G`/`T`, frozen when the spot opens), StartDate, StartReason (tinyint:
FreeAgent/Draft/Trade), StartTradeId (FK null), StartDraftPickId (FK null), EndDate
(null), **EndReason (tinyint null: Release/Trade/Draft)**, EndTradeId (FK null),
**ProtectionStatus (tinyint, default 0)**, OpenedUtc, ClosedUtc
→ CHECK `CK_RosterSpots_PlayerOrFranchise`: exactly one of the two is set, and it
agrees with PositionGroup
→ unique **filtered** `(LeagueId, PlayerId) WHERE EndDate IS NULL AND PlayerId IS NOT NULL`
→ unique **filtered** `(LeagueId, FranchiseAbbrev) WHERE EndDate IS NULL AND FranchiseAbbrev IS NOT NULL`
→ unique **filtered** `(TeamId) WHERE EndDate IS NULL AND PositionGroup = 'T'`
→ index (TeamId) WHERE EndDate IS NULL; (LeagueId, StartDate, EndDate)

`EndReason.Draft` — lost in the steal draft. Written by `DraftEndpoints.cs:475`,
which closes the loser's spot and opens the thief's through the same
`RosterChange.ApplyAsync` a trade uses; a steal is no new mutation path.

> **`ProtectionStatus`** (`Unprotected` 0 / `Protected` 1) — has the GM spent one of
> his protection slots on this spot. **The GM's decision, nothing else.**
> "Auto-protected" is deliberately absent: that is a fact about the **player**,
> derived from `CareerNhlGames`, and it applies to a free agent with no spot at all.
> Folding it in would destroy the distinction the protection phase needs most —
> "untouchable anyway" and "the GM burned a slot on him" are different answers.
> **A column and not a row per draft**, because the value is ephemeral: worth one
> summer, expiring at the start of the season it protected. No history to keep,
> only a slate to wipe — `protection-reset`'s job.

> The first filtered index makes "one player, one owner per league" a **database
> constraint**; the application check survives only to produce a readable message. A
> spot is **closed, never deleted**: the team keeps forever what that player banked
> for it.
>
> **`StartDate` and `EndDate` can be in the future.** Accepting a trade executes it
> immediately, dated to the following Monday: the outgoing spot gets an `EndDate` on
> Sunday, the incoming one a `StartDate` on Monday, which lets a GM set next week's
> lineup with the roster he will actually have. Consequence: "never closed" has
> stopped meaning "held today" — the three questions live in `RosterWindow` (see
> [scoring-model.md](scoring-model.md) §4) and the old filter now means
> **committed**. The filtered indexes stay correct: the outgoing spot leaves the
> filter at the very moment the incoming one enters, so never two owners.
>
> **A spot holds a player *or* an NHL franchise** (the Équipe slot, group `T`).
> Three details that are not details:
>
> - ⚠️ `PlayerId IS NOT NULL` in the first index's filter is **mandatory**: SQL
>   Server treats two NULLs as equal in a unique index, so without it the second
>   team in a league to open an Équipe spot would collide with the first.
> - The unique index `(TeamId) WHERE PositionGroup = 'T'` is why the Équipe slot has
>   **no** active/bench control: one franchise, one seat, no decision to make. It
>   also makes a franchise-for-player trade impossible rather than refused.
> - ⚠️ It is declared through the named overload `HasIndex([...], "name")`. Two
>   `HasIndex(x => x.TeamId)` calls do not make two indexes, they redefine one — the
>   second silently turned "a team's current roster" into "a team may hold only one
>   open spot".
>
> ⚠️ A nullable `PlayerId` propagates: any list of player ids built from spots must
> filter `PlayerId != null` before it feeds a `NOT IN`. One NULL makes `NOT IN`
> evaluate to NULL for **every** row — an empty result set, with nothing in the logs
> to say why (`LineupEndpoints.cs:207`).

**`RosterAssignments`** — `RosterAssignmentId`, RosterSpotId (FK cascade), PeriodId
(FK, NO ACTION), IsActive, EffectiveFrom, EffectiveTo (the window actually owned,
from `StatWindow.Intersect`), the 14 aggregated statistics of the period, **plus
TeamWins / TeamLosses / TeamOtLosses** for the Équipe slot, **FantasyPoints**,
GamesPlayed, IsFinalized, ScoredUtc
→ unique (RosterSpotId, PeriodId), which makes the nightly job safe to re-run: it
upserts on this key instead of accumulating
→ index (PeriodId, IsActive) INCLUDE (RosterSpotId, FantasyPoints, GamesPlayed)

> The three team columns are **distinct** from the goalie's Wins/OtLosses. "My
> goalie won" and "my franchise won" are different events on the same night, and a
> league pricing them differently has no way to say so if they share a column.
>
> **Banking lives here**: `IsFinalized` + `Period.FinalizedUtc`. A finalized row is
> never recomputed — a change to the scale does not rewrite the past.

**`TeamPeriodLineups`** — PK (TeamId, PeriodId), SetBy (`auto` | username),
SubmittedUtc → carries the "automatic lineup" information the UI shows.

**`DraftPicks`** — `DraftPickId`, LeagueId (FK cascade), Year, Round, PickInRound
(null), OriginalTeamId (FK), CurrentTeamId (FK), PlayerId (FK null), UsedUtc,
CreatedUtc → unique (LeagueId, Year, Round, OriginalTeamId) — one pick per team per
round per year, before any trading; index (CurrentTeamId, Year, Round). Current
owner distinct from origin gives "PIT's 2nd round via BOS" for free.

**`DraftSelections`** — `DraftSelectionId`, LeagueSeasonId (FK cascade),
OverallIndex (0-based, continuous across both segments), Segment (tinyint), Round
(1-based within its segment, stored), TeamId (FK), PlayerId (FK null = turn passed),
StolenFromTeamId (FK null), DraftPickId (FK null), MadeUtc
→ unique (LeagueSeasonId, OverallIndex) — `UX_DraftSelections_OneSelectionPerTurn`
→ unique (DraftPickId) filtered — an entitlement is spent once
→ unique (LeagueSeasonId, PlayerId) filtered — nobody is drafted twice
→ index (LeagueSeasonId, StolenFromTeamId) — the losses quota, recomputed each turn
→ CHECK: a steal carries no `DraftPickId`; a rookie pick carries one and steals from
  nobody; a passed turn steals from nobody

> **Why this table rather than a derivation.** Reading the draft back out of the
> `RosterSpots` it opened does not work: they carry `StartReason = Draft`, but
> `SeedMordusJob` opened all 418 spots of the Mordus import with that same reason,
> so a derivation would count 418 phantom selections before the first real one. Even
> in a clean league a steal is a **pair** of spots with no key for the event between
> them, and the draft's order would rest on an identity column's ordering.
>
> The load-bearing reason: steal turns are not tradable, so they have **no
> entitlement row to claim**. Without a clock, two GMs can submit at the same instant
> both believing they are on turn 7, with two different players, and no other
> constraint would notice. `UX_DraftSelections_OneSelectionPerTurn` is the only thing
> that stops it. Same distinction as `Trades` alongside `RosterSpot.StartTradeId`: a
> spot records **ownership over time**, this records **an event in a sequence**.
> `Round` is stored, not derived — the steal segment's round is arithmetic on
> `OverallIndex`, the rookie segment's comes from a tradable entitlement, and no
> single expression yields both.

**`Trades`** — `TradeId`, LeagueId (FK cascade), ProposerTeamId, CounterpartyTeamId,
Status (tinyint: Pending/Declined/Cancelled/Accepted/Processed), CreatedUtc,
RespondedUtc, ProcessedUtc, EffectiveDate (always a period start, null until
processed) → index (LeagueId, CreatedUtc DESC); index on Status filtered `= 3` —
everything accepted and waiting for a period boundary, the nightly job's query.

**`TradeAssets`** — `TradeAssetId`, TradeId (FK cascade), FromTeamId, ToTeamId,
AssetType (Player/Pick/Franchise), PlayerId (FK null), DraftPickId (FK null),
FranchiseAbbrev (FK null) → index (TradeId)
→ CHECK `CK_TradeAssets_ExactlyOneAsset`: exactly one of the three is set and agrees
with `AssetType`; an all-null row would silently mean "an empty thing changed hands"

> One row per asset, with From/To **per asset** rather than on the trade. That makes
> a three-team trade possible with no schema change, without complicating the
> two-team case, and covers every combination of players, picks and franchises for
> free: the constraint is on the asset, never on the trade.

**`TradeVotes`** — PK (TradeId, UserId), FavoredTeamId (FK null = "fair"), VotedUtc.
Votes are permanent — a second is rejected, not an overwrite, and the key is the
backstop if the application check is bypassed. Storing the favoured *team* rather
than a proposer-relative rating is what makes a vote meaningful on its own.

**`Messages`** — `MessageId` (bigint), LeagueId (FK cascade), SenderUserId (FK),
RecipientUserId (FK), Body (nvarchar 1000), SentUtc, ReadUtc (null)
→ index (LeagueId, SenderUserId, RecipientUserId, SentUtc) to read a thread
→ filtered `IX_Messages_Unread` on (RecipientUserId, LeagueId) `WHERE ReadUtc IS NULL`

> **Threads are per league.** A user belongs to several pools; the same two people
> talking in two leagues have two conversations, because the context is the pool. It
> also keeps the contact list trivially correct: league membership, never a union
> across pools. **No `Conversations` table** — a thread is "the messages between
> these two users", read in both directions, and at a dozen GMs the join it would
> save is not worth the row it would cost. The grouping lives in
> `ConversationSummary` (Core), so it is tested without a database. **`ReadUtc`
> null = unread**, and that is the badge's entire query — no counter to keep in
> agreement with the rows, and the filtered index stays the size of what is actually
> unread rather than of the history, which only grows.
>
> ⚠️ **Both FKs to `Users` are NO ACTION**, and not only out of this schema's
> conservative-delete habit: two cascade paths to the same table is the "may cause
> cycles or multiple cascade paths" error, and SQL Server refuses to create the
> constraint at all.

**`CockcoinAwards`** — `CockcoinAwardId` (bigint), UserId (FK cascade), Amount,
Reason (short stable code, not display text), AwardedUtc → index (UserId). A ledger,
not a mutable running total: a balance derived by SUM over an honest event log
cannot drift, and it doubles as a free audit trail. See
[cockman-concept.md](cockman-concept.md).

## Views

Mapped keyless so LINQ can compose filters onto them (`.Where(x => x.LeagueId ==
id)` joins the same query plan) while EF never tracks, inserts or migrates them.
Their definitions live in the migration that creates them.

| View | What it gives |
|---|---|
| `vPlayerSeasonStats` | season totals per player (`GameType = 2` — regular season only, by rule) |
| `vRosterSpotTotals` | points and games per spot **this season**, active and bench separated |
| `vTeamPeriodScores` | active/bench points per team per week → the weekly history |
| `vStandings` | SUM per team, cap and roster size **today** and **committed**, roster games |
| `vPoolerTradeRecord` | per team: processed trades won/lost/fair by member vote, plus a 0-100 `TraderRating` centred at 50 — null until one decided trade, so "no data" and "dead even" never look alike |
| `vCockcoinBalance` | SUM of `CockcoinAwards` per user |

Totals **as of a date** (test mode) are parameterised queries, not views — the same
aggregation with `WHERE GameDate <= @asOf`.

> **`vStandings` and `vRosterSpotTotals` filter by season.** A keeper-pool
> `RosterSpot` survives a season boundary, so without the filter both would sum
> *every* season's `RosterAssignments` together — a score that never resets. The
> filter joins `Periods` and compares `p.Season` to `Leagues.Season`. A future
> lifetime/career feature does **not** go through these two views; it reads
> `RosterAssignments` unfiltered. `vRosterSpotTotals` keeps a `LEFT JOIN` chain so a
> spot with no assignments yet still produces a row of zeroes rather than vanish.
>
> **`vStandings` is the only view that knows what day it is.** Its `Today` CTE reads
> the same cursor as everyone else: `SimulationState.AsOfDate + 1` when a replay is
> running, the real Eastern date otherwise. It needs that because a spot can start
> or end in the future: the displayed cap counts only spots **active today** — a
> player promised in a trade still counts, his replacement does not yet — while
> `EngagedCapTotal`/`EngagedPlayerCount` count every open spot. Both come from the
> same statement at one filter's difference, so they cannot diverge. Équipe spots
> are excluded from both: a franchise costs no cap and fills no roster slot. The
> `Scoring` CTE is unaffected by "today" — it sums `RosterAssignments`, which exist
> only for weeks already reached.

---

`.claude/doc/golden-scores-preSql.json` is the pre-SQL score snapshot used to
validate the scoring engine against its predecessor; kept only as a record.
