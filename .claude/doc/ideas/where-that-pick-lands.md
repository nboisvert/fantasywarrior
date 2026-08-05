# Where That Pick Lands

> The 2027 first you took off Rochette in November is currently the second
> overall pick, because Rochette is thirteenth — and the number moves every
> Monday without you doing anything at all.

## Why this hooks them

Anticipation, with a streak of malice in it. Every other asset in this pool gets
better when *you* do well. A pick you took off another GM gets better when *he*
does badly, and there is nothing in the world quite like owning a stake in
somebody else's collapse. It is the only line in the app a GM will check hoping
for bad news about a friend.

Today `DraftPicks` is dead weight. The rows exist, they are tradable, and
`OriginalTeamId` faithfully preserves "Pittsburgh's 2nd, via Boston" — but
nothing anywhere renders a pick as anything more than a label, so a pick trades
for whatever the two GMs feel like, which in practice means it trades for
nothing. Give it a live slot number and it acquires a price. That price then
moves every Monday when the standings bank, which means the asset has a
*heartbeat* rather than a status.

There is a specific complementarity with the trade ledger worth naming: that
feature has to label picks-only trades `NO VERDICT`, because a pick banks no
points and never will. This is the answer to that gap — a pick cannot be judged
on production, but it can absolutely be judged on where it is landing, and the
two numbers sit one screen apart.

## The data behind it

| Source | What it gives |
|---|---|
| `DraftPicks` | `Year`, `Round`, `OriginalTeamId` (never changes), `CurrentTeamId` (changes on every trade), `PickInRound` (null), `UsedUtc` (never written) |
| `vStandings` | The current order — `Points`, and the tie-break inputs |
| `RosterAssignments` + `Periods` | Last Monday's order, as a period-bounded aggregation |
| `Trades` / `TradeAssets` | How it got here — already rendered on trade cards |

**In bold, because it is the one assumption in this file: the schema does not say
how draft order is determined.** `PickInRound` is null by design ("Position within
the round, once the order is known"), `ruleConfig.draftRounds` says how many
rounds there are and nothing about their order, and no draft mechanism exists.
This feature therefore rests on the near-universal keeper convention —
**reverse order of finish, same order every round, no lottery** — and it must
label every figure `if the season ended today`, every time it renders one.

I am **not** proposing a `Leagues.DraftOrderRule` column. A setting with one
possible value that nobody can change is worse than a documented convention, and
if Nick says Les Mordus runs a lottery the only thing that changes is the label
and a sentence in the subtitle. The number stays a lower bound and stays useful.

```sql
SELECT dp.DraftPickId, dp.Year, dp.Round, dp.CurrentTeamId, dp.OriginalTeamId,
       ROW_NUMBER() OVER (
         PARTITION BY dp.LeagueId, dp.Year, dp.Round
         ORDER BY s.Points ASC, s.PointsPerGame ASC
       ) AS SlotInRound
FROM DraftPicks dp
JOIN vStandings s ON s.TeamId = dp.OriginalTeamId
WHERE dp.LeagueId = @leagueId AND dp.UsedUtc IS NULL
```

Overall number is `(Round - 1) * teamCount + SlotInRound`. The tie-break on
points-per-game exists only so the figure never flickers between two renders;
exact ties on a season-long float sum are near-impossible and the rule is there
for determinism, not fairness.

**Movement needs a baseline, and it does not need a snapshot table.** "Where this
pick sat last Monday" is the same window function over the same standings bounded
to periods `< current`, which is exactly the aggregation `vTeamPeriodScores`
already supports. A weekly snapshot table would be a row that must be written
exactly once, forever, to answer a question a `SUM` with a `WHERE` already
answers — the same mistake as the Firestore season-stats cache, in miniature.

## What it looks like

**Trades screen**, as a collapsible section above the trade history, titled
`Picks` with `BriefcaseIcon` at 16px. Subtitle, muted, always present:
*Order of finish, if the season ended today.*

That placement is a decision, not a default: a pick's only available verb is
"trade" — `UsedUtc` is never written and nothing converts a pick into a player —
so it belongs on the screen where it can be moved, not beside a roster it can
never join.

One row per pick **you own**, sorted by projected overall:

- **Far left**: `2027 R1` in Russo One.
- **Line 1**: the projected slot — `#2 overall`.
- **Line 2**, muted `#8b96ab`: provenance — `via Rochette (13th)`, or `own` when
  `OriginalTeamId = CurrentTeamId`.
- **Far right**: movement since last Monday — `▲3` in `--success` `#4ade80` when
  the slot improved, `▼2` in rose `#f43f5e` when it worsened, `—` when it did not
  move. `ArrowUpIcon` / `ArrowDownIcon` at 12px, both already in `Icons.tsx`.

**Row convention, decided here so Macklin has an answer to confirm rather than a
question to ask**: **two lines**, **the movement arrow on the far right**, and
**no position indicator of any kind**. Two lines because the provenance is the
half that makes the slot interesting — `#2 overall` alone is a number, `#2
overall, via Rochette` is a story. No position indicator because a pick has no
position, and inventing a fourth pattern would break the two-pattern rule
outright.

A second sub-section, collapsed by default: **`Picks you sent`** — the picks
whose `OriginalTeamId` is you and whose `CurrentTeamId` is not, with their
projected slot and the same arrow, coloured **inverted** (your own pick climbing
is bad news for you and renders in rose). This is the masochistic half and it is
the half that gets the section opened.

**One string in `CreateTradeSheet`**: when a pick is added to an offer, its row
shows `#2 overall (proj.)` inline, so a pick stops being an abstraction at the
exact moment it is being priced.

No new icons. No new colours.

## The rules

- **Every figure is labelled `if the season ended today`**, in the section
  subtitle and again on the trade sheet. This is not optional politeness; it is
  the difference between a projection the app owns and a convention the league
  owns.
- **No lottery is modelled.** If Les Mordus runs one, every number here is a lower
  bound on where the pick lands and the subtitle should say so. Nothing else in
  the implementation changes, which is why this can ship before Nick answers.
- **Before three banked weeks**, the section renders `Order not meaningful yet`
  and no slots at all. In week 1 every team has zero points and the window
  function would emit fourteen confident, arbitrary numbers — which is worse than
  showing nothing, and would be believed.
- **Dead weeks** (`Periods.GameCount = 0`): the standings do not move, so no pick
  moves, and the arrow renders `—` rather than `▲0`. A zero arrow implies motion
  that did not happen.
- **Rounds beyond the first** assume the same order every round — straight, not
  serpentine. Stated because serpentine is the other common convention and
  swapping to it is a one-line change to the overall arithmetic.
- **A GM holding two picks in one round** gets both rows, both sorted by
  projected overall. No merging, no "2 × R2" summary.
- **A pick already used** (`UsedUtc IS NOT NULL`) is excluded. This never happens
  today and the filter costs nothing.
- **A pick whose original team was never generated** cannot exist — the unique
  index `(LeagueId, Year, Round, OriginalTeamId)` and `draft-picks-init` see to
  that — and teams are never deleted, so there is no orphan case to handle.
- **A league where `draft-picks-init` has not run**: no rows, section absent
  rather than empty.
- **A pick traded during the week**: `ProcessTradesJob` writes `CurrentTeamId`
  directly, with no roster spot involved, at the same week boundary as everything
  else. The section follows at the next rollover and needs no special case.
- **Banking**: reads `vStandings` and period-bounded banked assignments, writes
  nothing. The "last Monday" baseline is an aggregation over finalized rows, so it
  is immutable by construction — it cannot drift, and it cannot be wrong twice.
- **Sim clock**: `vStandings` is already bounded by the simulated day, so the
  slots move week by week through a replay, which is also how this gets tested.
- **No authentication**: standings and pick ownership are already public. Showing
  another GM's picks is not an exposure, and `Picks you sent` deliberately shows
  him yours.

## What it costs

No migration. One query with a window function over `vStandings`, and a second
identical one bounded to the previous period for the arrow — both folded into the
trades payload the screen already fetches on mount, so no new endpoint and no new
spinner. The trade-sheet string reuses the same numbers. Frontend: one
collapsible section with two sub-lists in `Trades.tsx`, one inline label in
`CreateTradeSheet.tsx`. The pure logic worth mocking-free tests: the slot
computation with its tie-break, the overall arithmetic across rounds, the
three-week suppression, and the inverted colouring on picks you sent — all pure
functions over `(teamId, points, pointsPerGame)` tuples.

## What I rejected

- **A weekly standings-snapshot table to compute movement.** A row that must be
  written exactly once a week, forever, without fail, to answer a question a
  period-bounded `SUM` already answers. This is the Firestore score cache in
  miniature and it should die the same death.
- **Writing `PickInRound` nightly.** It turns a derived, moving figure into stored
  state that has to be corrected every Monday and would be wrong the moment the
  job is skipped. `PickInRound` is meant to hold the *final* order, once, when a
  draft actually exists — writing a projection into it would poison the column for
  its real purpose.
- **Adding `Leagues.DraftOrderRule`.** A setting with one value nobody can change
  is worse than a documented convention, and it invites a config screen for a
  decision the commissioner makes once every never.
- **A lottery simulator, or odds per slot.** A model, and a model of rules nobody
  in this league has written down. The moment the app publishes "you have a 14.2%
  chance at first overall" it owns a claim it cannot defend.
- **Projecting which player the pick becomes.** There are no prospect rankings in
  the schema and no source for them. This would be the app's first outright
  invention and it would be wrong in public every June.
- **Putting picks on the Team screen beside the roster.** A pick is not a roster
  spot, cannot be started, cannot be benched, and has no cap hit. Its only verb is
  "trade".
- **Hiding `Picks you sent`.** It is the half that hurts, which is the half that
  makes the section worth opening. Collapsed by default is the right compromise;
  absent is not.
- **A "pick value chart" converting slots to points.** Every version of this in
  existence is a curve fitted to somebody else's league, and it would put a
  confident number on the one asset whose whole appeal is that nobody knows what
  it is worth.
