# Who Won the Trade

> Every executed trade carries a running scoreboard of what each side has
> actually banked since the day it landed — printed directly under the vote the
> league cast at the time, which is frequently wrong.

## Why this hooks them

Vindication, and its uglier twin, schadenfreude. The moment fires the first
Monday after a trade's third banked week, when a card that has said *League
vote: Rochette, 9-3* for a month gains a second line saying *Ledger: Boisvert
+34*. Nine people publicly said you got robbed. The ledger says you did not, and
it will keep saying so, on that card, forever, without you having to bring it up
once.

That is the emotional inverse of The Perfect Lineup on purpose. Perfect Lineup
is regret you inflict on yourself in private; this is a verdict the league
handed down in public and now has to live with. It gives the winner a permanent
receipt and the losers a permanent record of having been wrong together — and
being wrong *as a group* is the only kind of wrong that fourteen friends will
relitigate at length.

It also does something structural: it puts a price on the vote. Right now
`TradeVotes` is an opinion with no consequence, worth a cockcoin and nothing
else. Once every vote is going to be graded against an outcome, voting stops
being a shrug and starts being a position you can be held to. The number to
watch is not any single trade — it is *the league's jury has been overruled on 5
of its last 8 verdicts*, which is a sentence that makes a group of die-hards
argue for an hour.

## The data behind it

Everything exists today. **Nothing here is missing from the schema, and no table
changes.**

| Source | What it gives |
|---|---|
| `RosterSpots.StartTradeId` | The spine of the whole thing: the spots a given trade *opened*, already carrying the receiving `TeamId`, `PlayerId` or `FranchiseAbbrev` |
| `RosterSpots.EndTradeId` / `EndDate` | When an acquired asset was moved on again — the one case that needs a label |
| `RosterAssignments` | `FantasyPoints`, `IsActive`, `IsFinalized`, `PeriodId` per spot per week |
| `Trades` | `Status = Processed`, `EffectiveDate`, `ProcessedUtc`, the two teams |
| `TradeAssets` | Only needed to name the **picks**, which never open a spot |
| `TradeVotes` | `FavoredTeamId`, `Magnitude` — what the league said at the time |
| `vPoolerTradeRecord` | The existing 0-100 `TraderRating`, built from votes only |
| `PlayerContracts`, `Leagues.DefaultCapHit` | The cap delta each side took on |
| `Periods.GameCount`, `FinalizedUtc` | Which weeks count toward the threshold |

`ProcessTradesJob` calls `RosterChange.ApplyAsync(..., tradeId: trade.TradeId)`
on both sides, for players and franchises alike, so `StartTradeId` is populated
for every asset that can score. That single column means the ledger does not
have to walk `TradeAssets` at all:

```sql
-- vTradeLedger: one row per side of one processed trade
SELECT rs.StartTradeId                                                 AS TradeId,
       rs.TeamId,
       SUM(CASE WHEN ra.IsActive = 1 THEN ra.FantasyPoints ELSE 0 END) AS ActivePoints,
       SUM(ra.FantasyPoints)                                           AS TotalPoints,
       COUNT(DISTINCT CASE WHEN p.GameCount > 0 THEN ra.PeriodId END)  AS LiveWeeksHeld
FROM RosterSpots rs
JOIN RosterAssignments ra ON ra.RosterSpotId = rs.RosterSpotId
JOIN Periods p            ON p.PeriodId      = ra.PeriodId
WHERE rs.StartTradeId IS NOT NULL
  AND ra.IsFinalized = 1
GROUP BY rs.StartTradeId, rs.TeamId
```

**Active points is the headline, not total production.** Two reasons, and the
second is the important one. First, active points *are* the standings — a ledger
that disagrees with `vStandings` would be a second truth about the same event.
Second, it hands the losing side a real rebuttal — "you only won because I
benched him" — and that rebuttal has a number waiting for it one screen away, in
Lineup IQ. The two ideas argue with each other on purpose. `TotalPoints` rides
along in the same view so the expanded card can show `34 / 41` and let the fight
happen with facts.

**Why the comparison is fair by construction, which is unusual.** Both spots
open on the same `EffectiveDate` — trades always land on a week boundary — so
both sides are measured over the identical set of weeks. And a player's
production is a property of the player, not of the roster he sits on: what he
scored for his new team is exactly what he would have scored for his old one.
There is no counterfactual to model here, which is why this number does not need
a projection and must never grow one.

One new view, **`vTradeLedger`**, in a migration that creates only a view — the
same shape as `20260803194349_TeamCommitmentsView`. A view rather than a stored
column for the reason the whole schema is: it is a `SUM` over an honest,
append-only log and it cannot drift. No new job. No nightly anything.

The cap delta is a second, smaller query over the same spots joined to
`PlayerContracts` for the league's season, with `DefaultCapHit` for the unsigned
— the same rule `vStandings` and `vTeamCommitments` already use, so the figure
reconciles with the cap gauge instead of quietly using a different convention.

## What it looks like

**Trades screen only.** No new bottom-nav slot, no new destination, nothing
linked from anywhere else.

### 1. The ledger strip on a processed trade card

`Trades.tsx` builds a `historyCard` per processed trade: `cardHead`,
`teamsSplitToggle`, `voteTeaser`. The ledger is **one new strip immediately
under `voteTeaser`**, because its whole rhetorical force comes from sitting
against the vote.

One line, three zones:

- **Far left**: `ScaleIcon` at 14px in `--muted`, then the word `LEDGER`, then
  the sample size in muted — `6 wks`.
- **Middle**: nothing. The line breathes; it is the payoff line on the card.
- **Far right**: the leading team's short name and the margin — `BOISVERT +34` —
  the name and number both in `--success` `#4ade80`. Within 3 points it reads
  `EVEN` in `--muted` `#8b96ab` with no name.

Under it, a muted second line carrying the cap consequence, right-aligned:
`took on +$3.4M`. In a $134M keeper league that is the standard rebuttal, and
the argument is malformed without it.

When the ledger leader is **not** the team the vote favoured, a chip sits at the
right end of the first line: `VOTE OVERRULED`, rose `#f43f5e` text on a
transparent rose-tinted pill, `ScaleIcon` inside. This chip is the entire
feature compressed into 14 pixels of height, and it is the thing a GM will
screenshot.

`ScaleIcon` is new and must be added to `Icons.tsx` (Lucide `scale`).
**Deliberately not `GavelIcon`** — that already means *suspended* on the Team
grid, and one glyph meaning two things is how a design system starts lying.

### 2. The expanded card

The existing expand (`teamsSplitToggle`) currently lists who each side
acquired. Every acquired asset gains a right-hand figure: what it has banked for
its new owner.

**Player-row convention, decided here so Macklin has an answer to confirm rather
than a question to ask**: **one line**, **compact position indicator**
(`.pos-compact-f/d/g` via `posGroupClass()`), **truncated name** (`N.
Kucherov`), **points on the far right**. One line because the expanded card is
already a two-column split and a second line would halve the number of assets
visible on a phone. Compact because two side-by-side columns is the densest
layout on the screen, which is exactly the density the position rule assigns to
compact.

The far-right figure is `34` in ice cyan, with a muted `/41` after it **only
when the two differ** — that is the benching argument, and showing `/34` when
they are equal is noise.

Three asset kinds render differently on the right:

- **Player** — the number.
- **Franchise** — the `NhlTeams.LogoUrl` at 20px in place of a position
  indicator, then the abbrev, then the number. **There is no `.pos-compact-t`
  and there must not be one**: a franchise is not a position, and inventing a
  fourth pill colour would break the two-pattern rule outright. The logo is the
  only honest indicator, and we already store it.
- **Draft pick** — the label from `DraftPicks` (`2027 2nd, via PIT`, which
  `OriginalTeamId` gives for free) and, on the right, `no verdict` in `--muted`.

### 3. One line in the Trades screen header

Next to the existing trade-record display, a second figure: **`Ledger 4-2`** —
settled trades this GM leads, against those he trails. It sits beside
`TraderRating` rather than replacing it, and the label under the pair reads
`opinion / outcome`. Two numbers that measure the same trades and disagree is
not a bug to fix; it is the product.

## The rules

- **A trade that is pending, accepted-but-unexecuted, declined or cancelled has
  no ledger at all** — not a zero, not a dash, no strip. Only `Status =
  Processed` qualifies, which also keeps the visibility rule intact: pending and
  declined trades are private (2026-07-23), processed ones are public, and the
  ledger inherits that without a new access rule.
- **Executed but no week banked yet**: the strip renders with `— not settled`
  in `--muted`. `NULL` and zero must not look alike, the same reasoning that
  makes `TraderRating` null until a decided trade.
- **The `VOTE OVERRULED` chip needs a threshold**: at least **3 banked weeks
  with `GameCount > 0`** and a margin of at least **10 points**. Numbers show
  from week one; the accusation waits. One noisy week must not be allowed to
  overrule the league, or the chip becomes wallpaper and stops meaning anything.
- **Dead weeks** (`Periods.GameCount = 0`, twice in 2025-26 for Milan-Cortina)
  contribute zero to both sides — correctly, since neither side played — and are
  **excluded from the week count** that gates the threshold. Otherwise a trade
  made on February 8th would be "settled" after a fortnight in which nothing
  happened.
- **The current, unbanked week is excluded entirely.** This is a deliberate
  contrast with Perfect Lineup, which does show its live week: a lineup verdict
  that moves on Saturday night is fun, but a *trade* verdict that flickers is
  cheapened by the flicker. A verdict should land once a week and then stand
  until next Monday.
- **An asset traded on again**: its spot closes with `EndTradeId` set, and its
  contribution to the first trade freezes there. The first trade is judged on
  what it delivered to that team *while that team held it*, which is the only
  defensible boundary. The card is labelled `re-traded` in `--muted` so nobody
  reads a frozen figure as a full comparison, and the threshold for
  `VOTE OVERRULED` is suspended on such trades permanently.
- **A player re-acquired later** opens a new spot with a new `StartTradeId`. He
  is not attributed back to the earlier trade. Correct, and it falls out of the
  schema without a rule.
- **Franchise trades work identically and need no special case.** The live COL ↔
  VGK swap is the first example: both spots are `PositionGroup = 'T'`, both
  produce one `RosterAssignment` a week, both bank. `Teams.FranchiseAbbrev` — the
  team's *identity* — is deliberately never consulted, because a GM who traded
  away his own colours is exactly the situation the two-fields decision was made
  for, and the ledger must describe what he owns, not who he is.
- **A picks-only trade** shows `NO VERDICT — picks only`, permanently, and never
  gets a chip. Honest, and it will start its own argument about whether picks
  can be judged at all. **Forward-compatible without a schema change**:
  `RosterSpots.StartDraftPickId` already exists, so the day the draft is built,
  the player a traded pick becomes opens a spot that traces back through the
  pick to the trade. The draft is not built (`DraftPick.UsedUtc` is never
  written), so this is a note, not a promise.
- **Non-NHL players** (the eight on Mordus rosters who have never played an NHL
  game) bank zeros. A trade that acquired one reads `0`, correctly and cruelly.
- **A brand-new team or a league with no processed trades**: no strip anywhere,
  and the header ledger record is **null**, rendered `—`, never `0-0`.
- **Banking compatibility**: reads only `IsFinalized = 1` rows and writes
  nothing at all. It cannot restate history. A mid-season scale change makes both
  sides a mix of two scales in the same proportion, since both sides are summed
  over the same weeks — the same caveat that already applies to the standings,
  and strictly milder, because this is a difference rather than a total.
- **No authentication**: the ledger is derived entirely from data already public
  to the league. It exposes nothing new, which is worth stating given DMs
  recently made this a live concern.

## What it costs

One migration that creates `vTradeLedger` and nothing else. No new endpoint:
`Trades.tsx` already fetches every trade once on mount, so the ledger figures
fold into that payload as two fields per side plus a per-asset array — giving it
its own fetch would add a spinner to a screen that currently has none, and the
detail is the same query at a finer grain, so it is one round trip either way.
The header record is a `GROUP BY TeamId` over the same view and rides on the
league payload beside `TraderRating`. Frontend: one strip in `historyCard`, a
right-hand figure in `tradeSide`, one figure in the screen header. The pure
logic worth mocking-free tests is small and real: the classification function
that turns (ledger margin, weeks held, vote outcome, re-traded flag) into one of
`not settled` / `even` / `leader` / `overruled`, including the picks-only and
dead-week cases.

## What I rejected

- **A full trade tree, CapWages-style, chaining every asset forward.** This is
  the seductive version and it is wrong here. The depth is unbounded, the
  recursion over `RosterSpots` is real work, and worst of all a trade's verdict
  would change because a *third* GM did something a year later. A verdict that
  someone else can move is not a verdict. The freeze-on-re-trade rule is the
  honest boundary, and it is one column.
- **Counting total production rather than active points.** It rewards a GM for
  acquiring a player and then benching him, and it produces a number that
  disagrees with the standings about the same weeks. Two truths, one event.
- **A value-over-replacement or "what would he have scored there" adjustment.**
  There is nothing to adjust: production is a property of the player, so the
  counterfactual and the measurement are the same number. Adding a model here
  would only add something to be wrong about.
- **Cost-per-point as the headline.** Computable, and genuinely interesting in a
  $134M keeper league — but a trade's verdict has to be one number a GM can
  shout across a room, and "1.4 points per million" is not it. The cap delta
  survives as the muted subline, which is where a rebuttal belongs.
- **A letter grade (A-, C+) on each trade.** Nobody has ever argued with a B.
  A signed integer with a name attached to it is what starts fights.
- **Replacing `TraderRating` with the ledger.** They measure different things —
  what the league thought, and what happened — and the gap between them is more
  interesting than either. Deleting the vote-based one would also delete the only
  thing cockcoins are currently awarded for.
- **A "your trade has been overruled" alert.** There is no push and no email,
  and even in-app it would be a notification whose only content is that someone
  else was right. The chip on the card is enough; let him find it.
- **Showing the ledger live during the current week.** Flicker cheapens a
  verdict. Perfect Lineup can afford a live week because it is a private regret;
  this one is a public ruling.
- **Grading individual voters** — "Rochette's votes have been wrong 6 of 9
  times". Tempting, computable from `TradeVotes` alone, and rejected on rule 4:
  with fourteen GMs and a handful of settled trades a season, the sample per
  person is three or four, which makes the number noise wearing a suit. The
  league-level version ("the jury has been overruled on 5 of 8") has fourteen
  times the sample and all of the fun.
