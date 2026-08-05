# The Colours You Own

> Fourteen NHL clubs belong to somebody in this league and eighteen belong to
> nobody, and one board shows who is quietly cashing two points a night off a
> club he is not even named after.

## Why this hooks them

Tribal pride, and its opposite — the small permanent shame of having sold your
flag. Nothing else in this pool touches identity: a roster is a portfolio, but
the club on your name is who you *are*, and in Les Mordus it has been who you are
for years.

The moment already happened. Vegas now owns Colorado and Colorado now owns Vegas,
while both teams are still called what they were called last month. Every night
`COL` wins, a man who has been Colorado in this pool since before the app existed
watches somebody else collect two points off it. There is no metric that captures
that and no metric needs to — it just has to be *rendered*, once, permanently,
on a board everybody reads.

The second hook is that the franchise market is the only **closed** market in the
pool. Every other asset has slack: there are 700 NHL players, there will be more
draft picks next year, a roster can grow to 35. There are exactly fourteen owned
clubs, one per GM, and a franchise can only ever be traded against another
franchise. Fixed supply, forced one-for-one, zero cap cost — that is the cleanest
negotiation surface in the product and today it is completely invisible. Put
fourteen clubs on a ranked list with a points column and somebody will want to
move up it.

## The data behind it

Everything exists today. **Nothing here is missing from the schema, and no
migration is needed.**

| Source | What it gives |
|---|---|
| `RosterSpots` where `PositionGroup = 'T'` | Who owns which club, now (`EndDate IS NULL`) and historically (never deleted), with `StartTradeId`/`EndTradeId` |
| `RosterAssignments` on those spots | `TeamWins`, `TeamLosses`, `TeamOtLosses`, `FantasyPoints`, `IsFinalized` per week |
| `Teams.FranchiseAbbrev` | The team's **identity**, which never moves — the divergence is `spot.FranchiseAbbrev <> team.FranchiseAbbrev` |
| `Games` | The club's real NHL record, `GameType = 2` only |
| `NhlTeams` | `Name`, `LogoUrl`, `DivisionName` |
| `RosterSpots` (player spots) + `Players.TeamAbbrev` | The homer count — how many of your own skaters play for the club you own |

Three aggregations, all trivial:

1. **Owned clubs**, one row each: the open `T` spot, its team, and
   `SUM(FantasyPoints)` over its assignments.
2. **The club's own record**, from `Games` — `FranchiseResults.For` is already
   the pure function that turns a set of games into wins/losses/OT losses and it
   is already tested. Reuse it verbatim rather than writing a second one; the
   board and the points column must not be able to disagree.
3. **The homer count**: open player spots for a team, joined to
   `Players.TeamAbbrev`, counted against the club that team's `T` spot holds.

No new view is needed and I would not add one: unlike `vTeamPeriodPerfect` or
`vTradeLedger`, exactly one screen consumes this and there is no second caller to
keep in agreement.

## What it looks like

**Standings screen**, as a collapsible section below the standings list, titled
`The Colours` with `ShieldIcon` at 16px. Subtitle, muted, shown once:
*Fourteen clubs are owned. Eighteen are not.*

**A deliberate resolution of a collision**: `perfect-lineup.md` proposes a
segmented control on this same screen, `Points | Lineup IQ`. Those two are *sorts
of the same fourteen rows*; this is a *different list of fourteen rows*. Mixing a
mode switch into a sort switch would produce a three-way control where one option
behaves unlike the other two, so this is a section and not a third segment.
Macklin should build it that way without asking.

One row per owned club, ranked by points banked this season:

- **Far left**: `NhlTeams.LogoUrl` at 24px, then the abbrev in Russo One.
- **Line 1**: the owning team's name, truncated.
- **Line 2**, muted `#8b96ab`: the club's NHL record, `14-6-3`.
- **Far right**: points banked, ice cyan. When a previous owner banked some of
  this season's points, a muted `+34 prev` sits under it — see the rules.
- **The divergence marker**: when the owner's own identity is a different club,
  an `ArrowLeftRightIcon` at 12px in rose `#f43f5e` immediately after the team
  name, with `aria-label="held by trade — Vegas by identity"`. That icon is the
  entire joke and it is permanent until somebody trades it back.

**Team-row convention, decided here**: two lines, truncated name, points on the
far right. Two lines because the NHL record is the context that makes the points
column mean something, and it cannot share a line with a team name on a phone.
**No position indicator anywhere in this section** — a franchise is not a
position, there is no `.pos-compact-t` and there must not be one, and the club
logo is the only honest indicator. We already store it.

Below the fourteen, one muted line: `18 clubs unowned`, expandable to a plain
list with their records. It exists to make the closed supply visible, and it must
carry **no claim button** — there is no free agency and a control that does
nothing is worse than a list.

**One line on the Team screen**, beside the Équipe slot: `7 of your 27 skate for
COL — league average 2.4.` That is the homer index, it is one string, and it is
the funniest number in the file.

## The rules

- **A league with no franchise spots** — every league but Les Mordus today — does
  not render the section at all. Absent, not empty.
- **Week 1, nothing banked**: the board renders with a `0` points column, and the
  NHL records are real from the first night. This section needs no history.
- **A franchise traded mid-season**: the points column shows what the club banked
  **for its current owner**, because banked points belong permanently to the team
  that held the spot and nothing may restate that. When a prior owner exists, the
  muted `+34 prev` suffix appears, so the club's true season total is visible
  without the ledger lying about who owns it. This is the rendering that makes the
  Vegas/Colorado swap legible on the board, and it is why the suffix exists at all.
- **Identity never changes.** No path in the app writes `Teams.FranchiseAbbrev`
  after seeding, by design (2026-08-05). A divergence is therefore permanent until
  a reverse trade, and the marker should never be dismissible.
- **The NHL record must be filtered to `GameType = 2`**, matching what
  `PeriodRollupJob` feeds `FranchiseResults`. A board showing playoff games beside
  a points column that excludes them would be two different truths in one row.
- **Live week included.** The points column shows banked plus the current week,
  matching `vStandings`, because this *is* a standings and the two have to agree.
  This is deliberately the opposite of `who-won-the-trade`, which excludes the
  live week — that one is a verdict and a verdict should not flicker; this one is
  a score and a score should move.
- **Dead weeks** (`Periods.GameCount = 0`): the club plays nothing, the record
  does not move, the row is unchanged. No special case, no `break` label needed —
  a row that does not move says it itself.
- **The Équipe slot has no active/bench control** and nothing in this section may
  imply one. The unique index `(TeamId) WHERE PositionGroup = 'T'` is why, and it
  is also why "one owner, one club" needs no application check.
- **The homer count**: a player with a null `Players.TeamAbbrev` — an unsigned
  free agent, an undrafted prospect — counts in the denominator and never in the
  numerator. The league average is over the fourteen teams that hold a franchise,
  not over all leagues.
- **A brand-new team in an existing league** with no franchise spot yet simply
  does not appear on the board, and its Team screen shows no homer line.
- **Banking**: reads finalized and live assignments, writes nothing, cannot
  restate a week.
- **No authentication**: every figure here is already public to the league.
  Nothing new is exposed.

## What it costs

No migration and no new view. Two small aggregations folded into the league
payload the Standings screen already renders from — it reads entirely from
`league.teams` today and would gain a parallel `league.franchises` array, so no
new fetch and no new spinner. The homer line is one extra count on the team
payload the Team screen already fetches. Frontend: one collapsible section in
`Standings.tsx` with an expandable unowned list, one string in `Stats.tsx`. No
new icons — `ShieldIcon` and `ArrowLeftRightIcon` both exist. The pure logic
worth mocking-free tests: the divergence predicate, the current-owner/prior-owner
points split, and the homer count against a null `TeamAbbrev`.

## What I rejected

- **A "Franchises" bottom-nav tab.** The nav is full at four and fourteen rows do
  not deserve a destination. This is a fact about the standings and it belongs
  under them.
- **Auto-renaming a team when the franchise it holds changes.** The whole point
  of keeping `Teams.FranchiseAbbrev` separate from the `T` spot (2026-08-05) is
  that the club you *are* and the club you *own* are allowed to diverge. Renaming
  would delete the joke this feature exists to render.
- **A loyalty bonus or penalty for holding your own colours.** Changing the
  scoring scale to reward sentiment is precisely what `scoring-model.md` exists to
  prevent, and it would make the first franchise trade retroactively expensive.
- **Letting a franchise be benched, or a team hold two.** Both are forbidden by
  the unique index, deliberately, and the slot's entire "no decision to make"
  property depends on that staying true.
- **Franchise-for-player or franchise-for-pick trades.** Already impossible by
  rule, and it should stay impossible: a closed fourteen-for-fourteen market is
  what makes the asset an identity rather than a commodity. The moment a club can
  be sold for a second-round pick it is inventory.
- **Projecting a club's remaining points from its schedule.** That machinery
  belongs to `four-game-weeks` and duplicating it here would give the app two
  places that predict, which is one more than it should ever have.
- **A claim mechanism for the eighteen unowned clubs.** There is no free agency,
  nothing converts an unowned club into a spot, and rendering a button that does
  nothing is worse than rendering a list.
- **Showing every historical owner of every club as a "banner history".** Real,
  computable from the closed `T` spots, and worth nothing until this league has
  three seasons of them in the database. Revisit in 2028, not now.
