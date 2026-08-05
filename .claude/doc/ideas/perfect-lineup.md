# The Perfect Lineup

> Every Monday, a GM learns not just what he scored but what the exact roster he
> already owned would have scored if he had ticked the right thirteen names —
> and the gap has his name on it.

## Why this hooks them

Regret with no excuse in it. A trade you lost can be blamed on the other guy; a
player who got hurt can be blamed on luck. This number holds the roster
constant: same players, same week, same games — the only variable is the
decision, and the decision was yours. It fires at the exact moment the week
banks, which is already the pool's emotional peak, and it turns a settled result
into a fresh accusation.

The second wave is envy. Once it is a league-wide percentage, "I finished 4th
but I set my lineup better than the guy who won" becomes a claim someone will
actually make out loud, and the guy who won will have a number to answer with.

The third wave is that it reframes what a GM thinks he needs. A team at 84%
does not need a better winger; it needs to open the app on Sunday night. That is
a cheaper fix than a trade, and telling a GM so is how you get him to open the
app on Sunday night.

## The data behind it

Everything is in the schema today. **Nothing here is missing, and no migration
is needed.**

| Source | What it gives |
|---|---|
| `RosterAssignments` | One row per (spot, week) with `FantasyPoints`, `IsActive`, `IsFinalized` — the entire input |
| `RosterSpots.PositionGroup` | Which slot each row was competing for (`F`/`D`/`G`/`T`) |
| `Leagues.ActiveForwards` / `ActiveDefense` / `ActiveGoalies` | How many seats each group had |
| `Periods.GameCount`, `Periods.FinalizedUtc` | Dead weeks, and which weeks are banked |
| `TeamPeriodLineups.SetBy` | `'auto'` vs a username — whether the GM chose, or the job did |

The aggregation, per (team, period):

```sql
-- PerfectPoints: the best legal set, from the rows that already exist
SELECT SUM(FantasyPoints)
FROM (
  SELECT ra.FantasyPoints,
         ROW_NUMBER() OVER (
           PARTITION BY rs.TeamId, ra.PeriodId, rs.PositionGroup
           ORDER BY ra.FantasyPoints DESC
         ) AS rn,
         rs.PositionGroup
  FROM RosterAssignments ra
  JOIN RosterSpots rs ON rs.RosterSpotId = ra.RosterSpotId
) x
JOIN Leagues l ON ...
WHERE x.rn <= CASE x.PositionGroup
                WHEN 'D' THEN l.ActiveDefense
                WHEN 'G' THEN l.ActiveGoalies
                WHEN 'T' THEN 1
                ELSE l.ActiveForwards END
```

`ActualPoints` is the same rows filtered on `IsActive = 1`. The delta is the
week's regret.

**Lineup IQ = SUM(actual) / SUM(perfect) across banked weeks** — a ratio of
sums, deliberately not an average of weekly ratios. A 2-point week and a
90-point week must not carry the same weight, and a 0/0 dead week must not
count as a free 100%.

This wants **one new view, `vTeamPeriodPerfect`**, sitting beside
`vTeamPeriodScores` — one team, one period, `ActualPoints`, `PerfectPoints`.
A query alone would work, but the Standings roll-up and the Team-screen detail
both need it and would otherwise re-derive the same window function in two
places, which is exactly how two screens start disagreeing about the same
number. It is a view rather than a column for the reason the whole schema is:
it is a `SUM` over an honest log and cannot drift. The migration creates a view
and nothing else — the same shape as `20260803194349_TeamCommitmentsView`.

**Tie note**: two forwards tied on the boundary make `ROW_NUMBER` pick
arbitrarily, but the *sum* is identical either way, so the figure is
deterministic even when the chosen set is not. Worth a comment in the view or
someone files a bug against it.

## What it looks like

Three surfaces, two of which are one-liners. No new bottom-nav slot, no new
destination.

**1. Team screen — the home of the feature.** A new collapsible section under
the existing cap gauge, titled `Perfect Lineup`, with `ActivityIcon` at 16px.
Subtitle, muted, shown once: *Hindsight only — it does not know who was hurt.*

One row per banked week, most recent first:

- Left: `W6` in Russo One, with the week's dates beneath it in muted `#8b96ab`.
- Middle: `58 / 71`, the actual over the perfect, actual in ice cyan.
- Far right: the delta — `-13` in rose `#f43f5e`, or the word `PERFECT` in
  `--success` `#4ade80` when the gap is zero.
- A week the job filled itself carries a muted `auto` chip after the week
  number.

Tapping a row (`ChevronDownIcon`, 44px target) expands only the swaps that
mattered — for each position group, each benched row that outscored an active
row, paired:

```
  ↑ N. Kucherov      benched      9
  ↓ C. Suzuki        played       2
```

**Player-row convention, decided here so Macklin has an answer to confirm rather
than a question to ask**: one line per player, **compact position indicator**
(`.pos-compact-f/d/g`), **truncated name** (`N. Kucherov`), **fantasy points on
the far right**. Compact because this is the densest list on the screen — two
stacked comparison lines inside an expandable, on a phone — which is exactly the
density the position rule assigns to compact. Truncated because the arrow, the
verb and the number all have to fit on one line and the number is what the row
is for. `ArrowUpIcon` in `--success` on the benched line, `ArrowDownIcon` in
rose on the played line.

**2. Standings — the league comparison.** A segmented control above the list:
`Points | Lineup IQ`. It re-sorts the same `<ol>` and swaps the right-hand
figure from `312 pts` to `91.5%`, keeping the `+N this week` subline. The
screen is 53 lines today and `.standings-row` is reused verbatim. This is the
part that makes Standings worth opening more than once a week.

**3. GM Office — one string.** The `dash-leader-note` currently ends
`, 14 benched`. Replace that clause with `58 of a possible 71`. This is a
deletion as much as an addition: see below for why the number it replaces is
the wrong one.

## The rules

- **No banked weeks yet** (week 1, or a fresh league): the section renders
  `Nothing banked yet.` and Lineup IQ is **null, not 0** — the same reasoning
  that makes `PoolerTradeRecordView.TraderRating` null until a decided trade.
  "No data" and "perfectly awful" must never look alike.
- **Dead weeks** (`Periods.GameCount = 0` — the 2025-26 season has two, Feb 9-22
  for Milan-Cortina): perfect and actual are both zero. The week appears in the
  list labelled `break`, matching what the Dashboard already does, and is
  **excluded from the ratio entirely**. Including it would either divide by zero
  or hand out a free 100%.
- **The Équipe slot** contributes identically to both sides — it is always
  active and there is no decision to make (`LineupRules.LegalActiveSet` keeps it
  active whatever is submitted, and the unique index on `(TeamId) WHERE
  PositionGroup = 'T'` is why). Include it so both figures reconcile with the
  standings; **never list it in the swap detail**, because it can never be a
  swap.
- **A player acquired or traded away mid-week**: no special case is needed.
  `StatWindow.Intersect` already bounds his assignment to the days actually
  owned, so his `FantasyPoints` is what he was worth *to this team*. Activating
  him would have been legal and would have scored exactly that.
- **Non-NHL players** (the eight on Mordus rosters who have played no NHL game):
  they produce assignments of all zeros, so they never enter the perfect set and
  never create regret. A team carrying four of them will post a suspiciously
  high Lineup IQ because it had nothing better to bench. **This is correct and
  should not be corrected.** Lineup IQ measures decisions, not roster quality,
  and that distinction is the fight the feature is supposed to start.
- **Injuries**: **there is no historical injury data and there never will be
  from our sources** — `PlayerInjuries` is a snapshot of today. The metric will
  cheerfully say "you should have played Hellebuyck" on a night he was scratched.
  Say so once, in the section subtitle, and do not attempt to compensate. A
  fudged correction would be the one number in this app not derived from an
  event log.
- **Auto-filled weeks** stay counted, marked with an `auto` chip. The scoring
  model says a GM on vacation is not punished in *score*; he is still measured
  in *judgement*, and the chip lets the league decide how much slack to give.
  Excluding them would make the metric gameable by simply never submitting.
- **Banking compatibility**: this reads `FantasyPoints` off finalized rows and
  writes nothing at all. It cannot restate history. A mid-season scale change
  makes past weeks a mix of two scales — the same caveat that already applies to
  the standings, no worse, since both sides of the ratio come from the same rows.
- **The current unbanked week** is computable and should be shown, labelled
  `in progress` — half the pleasure is watching it move on Saturday night — but
  it stays out of the season Lineup IQ until it banks.

## What it costs

No migration to any table; one migration that creates only `vTeamPeriodPerfect`.
One new endpoint, `GET /api/leagues/{leagueId}/teams/{username}/perfect`,
returning the week array plus season totals plus the per-week swap detail — the
detail is the same query at a finer grain, so it is one round trip, not two. The
league-wide roll-up folds into the existing `GET /api/leagues/{leagueId}` payload
as a `lineupIq` field per team, because Standings already renders entirely from
`league.teams` and giving it its own fetch would add a spinner to a screen that
currently has none. Frontend: one collapsible section in `Stats.tsx`, one
segmented control in `Standings.tsx`, one string in `Dashboard.tsx`. The
top-N-per-group selection is a pure function over a list of
`(positionGroup, points)` — mocking-free tests, per the house rule, including
the tie case and the dead-week case.

## What I rejected

- **"Points left on the bench" as the headline.** This is the number the app
  shows today, and it is the wrong one: it counts a benched forward's points
  even when nine better forwards were already active. It flatters a deep bench
  and accuses nobody of an actual mistake. A GM whose 10th-best forward scored 6
  is told he "left 6 on the bench" when he could not legally have played him.
  The delta against the best legal set is the only version that is an
  accusation, and swapping one for the other is a net simplification of the
  Dashboard line.
- **Injury-adjusted regret.** Dead on rule 1. `PlayerInjuries` holds today's
  state with `ReportedUtc`/`ResolvedUtc` written by a scraper that reads today's
  page. There is no way to ask "was he out in November", and the project has
  already decided (correctly) not to chase one.
- **A per-player "you bench him too often" stat.** Computable from the same
  rows, but it accuses a player instead of a GM, and the GM is who reads this.
- **Auto-suggesting next week's lineup from the same machinery.** A different
  feature, and it would turn the number from a verdict into a tool. A verdict
  starts fights; a tool ends them, and fights are the product.
- **Excluding auto-filled weeks from the ratio.** Makes the metric gameable by
  never submitting a lineup — the one behaviour it should least reward.
- **Averaging the weekly percentages** instead of a ratio of sums. Lets a
  single quiet week where a GM went 4/5 outweigh a 90-point week where he went
  70/90.
- **Making it a new bottom-nav tab.** The nav is full at four, and there is no
  version of this that is not a fact *about* a team, which is what the Team
  screen is for.
