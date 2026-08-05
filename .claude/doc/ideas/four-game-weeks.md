# Four-Game Weeks

> Beside every player on the roster, one number: how many games he actually
> plays in the week that is about to lock — because the 0.5-a-game winger with
> four games beats the 0.9-a-game winger with two, and right now nobody in this
> league can see that.

## Why this hooks them

Greed, and the particular greed of finding an edge nobody else has looked for
yet. The moment fires Sunday night before the Monday-00:00 lock: a GM opens the
Team screen to rubber-stamp last week's lineup, sees `2` next to his second-line
centre and `4` next to the winger he has had on the bench since October, and
does not rubber-stamp anything. He has just found free points in a roster he
already owned, without trading for anything.

Nothing in this app currently fires before the lock. Perfect Lineup fires
Monday, the trade ledger fires Monday, banking fires Monday — the whole product
is retrospective, and the one compulsory weekly action a GM must take has no
information attached to it whatsoever. He sets his lineup from memory of who is
good. This is the number that makes Sunday night a visit instead of a chore, and
it is the only idea in this folder whose payoff is *before* the week rather than
after it.

The second wave is dread, which is more useful than it sounds. A GM who realises
schedule density is a lever also realises everyone else can see the same column,
and that the guy who checks every Sunday is going to beat him by four points a
week for no reason other than showing up. That is the exact behaviour the whole
app is trying to buy.

The third wave feeds trades, and it fires mid-week rather than Sunday. Because
the column is visible from Monday, a GM who sees his star has a two-game week
*next* week has a reason to shop him *this* week — and a roster stacked on one
NHL club becomes visibly fragile in a way a PDF could never show. "You've got
five Avalanche and they play twice" is a trade opening.

## The data behind it

**The future schedule is not in the database today, and this is the one thing
here that has to be built.** `StatsSyncJob` filters every day's games on
`FinishedStates = ["OFF", "FINAL"]`, so `Games` has only ever held games that
have been played. Everything else the feature needs already exists.

**It needs no migration, which is the surprising part.** `Game.LastPeriodType`
is already `string?`, and `FranchiseResults.IsFinal` already treats a null
`LastPeriodType` as *scheduled, in progress or postponed — not a 0-0 regulation
loss for both sides*, with a comment saying exactly that and a test pinning it.
The guard against unplayed rows was written before there were any. So a
scheduled game can be inserted into `Games` with a null `LastPeriodType`, zero
scores, and no `PlayerGameStats` children, and nothing downstream misreads it.

I checked every reader, because this is the claim the idea lives or dies on.
There are three, plus the views:

| Reader | What an unplayed row does to it |
|---|---|
| `StatsSyncJob` | Upserts by `GameId` (`existingGames.TryGetValue` → `ApplyGame`), so the placeholder is *overwritten in place* the night the game finishes. No delete rule to get wrong. |
| `PeriodRollupJob` | Passes rows to `FranchiseResults.For`, which skips them on `IsFinal`. Unaffected. |
| `PeriodInitJob` | Counts games per day to set `Periods.GameCount` — this **improves**, since `GameCount` is only correct today if the whole season has already been backfilled. The job is append-only, so it can never restate a week that already exists. |
| `vPlayerSeasonStats`, `vRosterSpotTotals`, `vTeamPeriodScores`, `vStandings`, `vTeamCommitments` | **None of them join `Games`.** Every `GamesPlayed` in the view SQL is a `COUNT(*)` over `PlayerGameStats` or a `SUM` over `RosterAssignments`. |

So: one new console job, **`schedule-sync`**, appended to `daily-jobs.yml` ahead
of `nightly`. It reads the NHL schedule for the next ~3 weeks and upserts `Games`
rows for `GameType = 2` fixtures that have no result yet. It is the smallest job
in the repo and it reuses `NhlApiClient` verbatim.

Once the rows exist, the three numbers are ordinary queries:

- **Games next week** — `Games` in `[Period.StartDate, Period.EndDate]` for the
  next period, where `HomeTeamAbbrev = p.TeamAbbrev OR AwayTeamAbbrev =
  p.TeamAbbrev`, joined to the team's open `RosterSpots` through
  `Players.TeamAbbrev`. Plus the same count for the Équipe slot's
  `RosterSpots.FranchiseAbbrev`.
- **Opponent tier** — from *finished* `Games` this season: each franchise's goals
  allowed per game, `SUM(CASE WHEN home THEN AwayScore ELSE HomeScore END) /
  COUNT(*)`. Split into terciles across the 32 clubs, once, and used as a colour.
- **Goalie start share** — `PlayerGameStats.Starter` summed over the season for
  that goalie, against his club's games played. `9/20 GS`. This is the honest
  half of the goalie problem and we already store it.

**No projection is computed anywhere, and none may be added.** The GM is given
two facts — how many games, against whom — and does the multiplication himself.
That is not laziness: Perfect Lineup already established that this app ships
verdicts and facts, and a fact cannot be discredited. The first time a projected
total says "bench Kucherov" and Kucherov puts up six, a projection is dead
forever; "Tampa plays twice" is still true that Monday.

## What it looks like

**Team screen (`Stats.tsx`), where lineups are actually set.** No new screen, no
new nav slot, nothing linked from anywhere else.

### 1. One column on the roster grid

The grid carries 21 columns behind a horizontal scroll. The new column, headed
`NXT`, sits **immediately after the sticky identity cell** — the second column,
before every stat — so it is on screen without scrolling. That placement is the
whole design decision: a number a GM has to scroll to find is a number he will
not use on a Sunday at 23:40.

Colour by value, and the scale is deliberately not a gradient:

| Value | Colour | Why |
|---|---|---|
| `4`+ | `--success` `#4ade80` | The thing you are hunting |
| `3` | default text | The median week; no signal |
| `2` | `--muted` `#8b96ab` | Quietly bad |
| `0`–`1` | rose `#f43f5e` | Rose means *do not do this* everywhere in this app — over cap, injured. Starting a player who plays once is the same class of mistake. |

`—` in `--muted` when the schedule is not known, which is **never the same as
`0`**.

### 2. The lineup picker — where it earns its keep

`LineupPicker` is the overlay that opens on a lineup toggle and lists the legal
replacements in the same position group. It is the literal instant of decision,
two players side by side, and it currently shows a name and a subline.

Each option row gains the next-week game count on the far right and the
opponents underneath the name.

**Player-row convention, decided here so Macklin has an answer to confirm rather
than a question to ask**: **two lines**, **truncated name** (`N. Kucherov`) on
line one, **the opponent abbrevs** on line two, **the game count on the far
right**, vertically centred across both lines. Two lines because the opponent
string is the entire argument and cannot share a line with a name on a phone.
Truncated because the count is what the row is for. The existing compact
position indicator stays where it is — every option in the picker is already the
same position group, so the letter carries no information here and is not
repeated.

Line two reads `@ CGY · NSH · @ SJS · CHI`, each abbrev coloured by its tier:
`--success` for a bottom-tercile defence, default for the middle, `--muted` for
a top-tercile defence. Home and away are distinguished by a leading `@` only —
no icon, no second colour; the row already carries two colour meanings and a
third would be mud.

### 3. One line for the Équipe slot

Above the grid, a single line: the franchise's `LogoUrl` at 20px, the abbrev,
and `4 games` on the right. It carries no control, because the Équipe slot has
no active/bench decision to make — the unique index on `(TeamId) WHERE
PositionGroup = 'T'` is why. It is there because it is the only place in the app
that makes the franchise feel like a living asset between Mondays, and because
in a $2-a-win league a four-game week for your club is eight points nobody
currently anticipates.

### 4. The section header

`Next week — W8, Nov 24-30` with `CalendarIcon` at 16px, already in
`Icons.tsx`. **No new icon is needed anywhere in this feature.**

## The rules

- **Before `schedule-sync` has ever run**, or for any date it does not cover:
  every cell shows `—` in `--muted`, never `0`. "Unknown" and "plays nobody" are
  different statements and the whole feature is worthless if they render alike.
- **A player with a null `Players.TeamAbbrev`** — an unsigned free agent, an
  undrafted prospect, several of the 30 unsigned men on Mordus rosters — shows
  `0`, not `—`, with `no club` as the cell's aria-label. Here we genuinely know
  the answer, and it is zero. This is the one place where the two renderings are
  each correct for a different reason, and Macklin should not unify them.
- **Dead weeks** (`Periods.GameCount = 0`; 2025-26 has two, Feb 9-22 for
  Milan-Cortina): the header reads `Next week — break, nobody plays` and the
  column is suppressed entirely rather than filled with fourteen zeros. This is
  the single most valuable week for the feature to exist, because a GM who does
  not know will otherwise spend twenty minutes optimising a lineup for a week
  that cannot score.
- **The final week of the season** has no "next" period. The section hides
  itself; it does not render an empty grid column.
- **A player traded between NHL clubs mid-week**: `player-sync` refreshes
  `Players.TeamAbbrev` nightly, so his count follows him from the next run and is
  stale by at most a day. Acceptable and worth stating, because a GM who spots it
  will otherwise file it as a bug.
- **A player acquired in a pool trade**: no special case. His spot opens at the
  week boundary, so he appears in the grid the same Monday his games begin
  counting for his new owner.
- **Playoffs**: `GameType = 2` filtered in `schedule-sync` on write and again on
  read. Excluded by rule everywhere, and this is nowhere.
- **The column is visible from Monday, not only Sunday.** It always describes the
  *next* period, never the current one — the current week's lineup is locked and
  a count for it would be information a GM cannot act on. Showing next week all
  week is what turns it from a Sunday chore into a Wednesday trade motive.
- **During a replay** the whole 2025-26 season already sits in `Games` as
  finished rows, so the feature works end to end today with no job at all,
  bounded by `SimulationState.AsOfDate` like every other date-aware query. That
  is the build order: **ship the UI and the query against the replay, write
  `schedule-sync` second.** It also means the job's absence in production is a
  degradation to `—`, not a broken screen.
- **Banking compatibility**: this reads nothing scored and writes nothing scored.
  It cannot touch a `RosterAssignment`, a `FantasyPoints` or a `FinalizedUtc`.
  The only scoring-adjacent consequence is `Periods.GameCount` becoming accurate
  earlier in a fresh season, and `PeriodInitJob` is append-only, so it can never
  restate a week that already exists.
- **A brand-new team, week 1**: the feature needs no history at all. It is the
  only idea in this folder that is fully useful on day one, and the only one that
  works for a league founded next October with an empty `RosterAssignments`.
- **Opponent tiers need a sample.** Before ~10 games a club has played, the
  terciles are noise; until the season's median club has played 10 games, the
  abbrevs render in default colour with no tier. The count itself is exact from
  day one and is never suppressed.

## What it costs

**No migration.** One new console job, `schedule-sync`, perhaps 80 lines,
reusing `NhlApiClient` and the existing upsert shape from `StatsSyncJob`, added
to `daily-jobs.yml` before `nightly`. One endpoint extension rather than a new
endpoint: the lineup payload the Team screen already fetches gains
`gamesNextWeek` and an `opponents` array per entry, plus the Équipe line — the
grid and the picker read the same objects, so there is nothing to keep in sync
between them. Frontend: one column in `RosterGrid`, a second line and a
right-hand figure in `LineupPicker`, one header line, one section header. The
pure logic worth mocking-free tests is the tercile classifier (including the
too-few-games case) and the games-in-range counter (including the home/away OR,
the dead week and the null-club case). Honest total: a small job, a medium UI
change, and a careful read of one query.

## What I rejected

- **A projected point total for next week**, or an auto-suggested lineup. It is a
  model, and this app ships facts. Perfect Lineup already rejected the same thing
  for the same reason from the other direction: a tool ends arguments, and
  arguments are the product. There is also a practical asymmetry — a wrong
  projection discredits the feature permanently, whereas "Tampa plays twice"
  stays true no matter how the week goes.
- **A separate `ScheduledGames` table.** Safer on the whiteboard, worse in
  practice: it needs its own "delete the row when the game finishes" rule, and a
  second place in the codebase that believes it knows the NHL schedule. Deletion
  rules that must fire exactly once are precisely what rots. `Games` already has
  a tested null-guard written for this case, upserts by `GameId` for free, and
  exactly three readers, all of which I checked.
- **A `Games.IsScheduled` flag or a `GameState` column.** Redundant:
  `LastPeriodType IS NULL` already means it, is already the predicate
  `FranchiseResults` uses, and adding a second way to ask the same question
  invites the two to disagree.
- **Showing strength of schedule as a number** (opponent goals-against per game,
  `3.42`). Kept as the input to the colour, rejected as displayed text. A phone
  row cannot carry a second decimal beside a name, and no GM reasons in
  hundredths of a goal — he reasons in *soft, normal, tough*, which is what a
  tercile is.
- **Back-to-backs, rest days, travel.** All real in the NHL and all invisible
  here: in a weekly pool four games is four games whether they fall on
  consecutive nights or not. This would matter in a daily-lineup pool. Ours is
  weekly, and rule 5 is rule 5.
- **Projecting goalie starts.** We have `Starter` per game, so the *history* is
  honest and gets shown; guessing the next start is not, and there is no
  depth-chart source anywhere in the stack. Show the share, refuse the forecast.
  This is the same line drawn twice.
- **A league-wide "who has the best week ahead" leaderboard.** Dies on rule 4 and
  on taste. With fourteen GMs it would simply instruct thirteen of them to copy
  the leader, converting a private edge into a commodity in one screen — and the
  pleasure here is entirely in having noticed it yourself. It would also, with no
  authentication, be a public read of everyone's Sunday-night thinking.
- **A bye-week or two-game-week alert.** No push, no email, in-app only — and
  in-app it would be a notification whose content is already a coloured number
  two columns from where he is looking.
- **Adding the column to the Dashboard as a "set your lineup" nudge card.** The
  Team screen is already reachable from the bottom nav, and a second route to the
  same decision breaks the no-duplicate-destinations rule (2026-07-22). One path.
