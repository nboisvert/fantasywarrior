# What You Already Owe

> A second cap gauge, for next season, filled in before this one is over — and it
> says you have $119M committed to eleven men and no goalie.

## Why this hooks them

Dread. Specifically the keeper-league kind, which is different from every other
emotion in this app because it is about a season that has not started and cannot
be fixed with a lineup. A bad week costs a week. A bad contract costs three
years, a roster spot, and the cap room you needed to fix the other thing.

The moment fires twice. First on a Tuesday in November when a GM opens his Team
screen for an unrelated reason, sees `$119.4M / $134M — 2026-27` under the gauge
he already knows, and realises he is nearly capped out for a season he has not
played a game of. Second, and more usefully, inside the trade sheet: the app
today validates a trade against *this* season's cap and says nothing at all about
the four years attached to the winger being offered. One muted line — *next year:
+$13.5M, signed through 2029-30* — converts a rental into a mortgage in the two
seconds before Accept.

And it changes what a player is worth *today*, which is the part that feeds
trades. An expiring $8M forward and an identical forward signed for four years
are not the same asset, and right now nothing in the product can tell them apart.
Once it can, half the league discovers it has been valuing everyone wrong, which
is the most productive kind of argument this pool can have.

## The data behind it

Everything exists today. **Nothing here is missing from the schema, and no
migration is needed.**

`CapWagesSyncJob` writes **one row per contract-season**, not one per player —
its own summary line says "Wrote N new contract-seasons", the parser tests show
Crosby carrying rows from 2005-06 through 2025-26, and the decisions log cites
Eichel at $10M in 2025-26 and $13.5M from 2026-27. So `PlayerContracts WHERE
Season = '20262027'` is already populated for everybody under contract, and it
was populated as a side effect of a job that runs for a different reason.

| Source | What it gives |
|---|---|
| `PlayerContracts` | `Season`, `CapHit`, `ClauseType` — one row per season of the deal |
| `RosterSpots` open player spots | Who you will still have |
| `Trades` accepted-but-unexecuted | Who you have already promised to have |
| `Leagues.CapAmount`, `.DefaultCapHit` | $134M and $1M |
| `Players.PositionGroup`, `Leagues.ActiveGoalies` etc. | Whether what you owe is even a legal team |

**The three buckets, and the reason they are three and not two.** For every
currently-owned player, ask whether he has a `PlayerContracts` row for season
N and for season N+1:

| N+1 row | N row | Bucket | Costs next year |
|---|---|---|---|
| yes | — | **Signed** | its `CapHit` |
| no | yes | **Expiring** | **$0** |
| no | no | **Unsigned** | `DefaultCapHit` |

This is where the feature is easiest to get wrong, and the wrong version is the
one you get by copy-pasting `vStandings`. For *this* season, a missing contract
means "we do not know what he costs", and the league's house rule (2026-08-05) is
to charge $1M rather than pretend he is free. For *next* season, a missing row on
a man who **has** a current deal means something completely different: **his
contract ends**. That is information, not a gap, and charging it $1M would
silently convert "you lose him" into "he costs a million" — the exact error the
$1M rule was invented to fix, running in reverse. A player with **no row for any
season** is genuinely unsigned, which the 2026-08-05 decision established is a
permanent and ordinary state, so he keeps costing `DefaultCapHit`.

**Use the presence of a season row, never `PlayerContracts.YearsRemaining`.**
That column is a snapshot taken at import time and it starts lying the moment a
season turns; the rows themselves cannot. This is worth a comment in the code,
because `YearsRemaining` is right there and looks like the obvious answer.

## What it looks like

**Team screen**, inside the collapsible cap section that absorbed the retired
Roster screen on 2026-07-26. No new screen, no new nav slot, nothing linked from
anywhere.

**1. A second gauge**, directly under the existing one, labelled
`2026-27 committed` with `BriefcaseIcon` at 16px. Same bar component, same
colours: ice cyan fill, rose `#f43f5e` fill and figure when it exceeds
`CapAmount`. Figure reads `$119.4M / $134M`.

**2. Three counts under it**, as muted chips: `11 signed · 9 expiring · 3
unsigned`. Tapping the gauge (44px target, `ChevronDownIcon`) expands a list
**grouped by those three buckets in that order**, each group sorted by cap hit
descending.

**Player-row convention, decided here so Macklin has an answer to confirm rather
than a question to ask**: **one line**, **compact position indicator**
(`.pos-compact-f/d/g` via `posGroupClass()`), **truncated name** (`J. Eichel`),
**next season's cap hit on the far right**. One line because this list can run to
thirty rows inside an already-collapsible section, and a GM is scanning for big
numbers, not reading biographies. Compact position because a thirty-row list
inside a collapsible is the densest thing on the screen.

Far right by bucket: **Signed** → `$13.5M` in default text. **Expiring** → the
*current* hit in `--muted` followed by an `EXP` chip, because a column of `$0.0M`
is unreadable and what he costs today is precisely what you are about to lose.
**Unsigned** → `$1.0M` in `--muted` with the same `assumed` treatment
`vStandings.UnknownContracts` already established.

**3. The line that produces the dread**, in rose, immediately under the chips,
only when true: `No goalie signed for 2026-27.` Derived from the signed bucket's
`PositionGroup` counts against `Leagues.ActiveGoalies` / `ActiveDefense` /
`ActiveForwards`. One sentence, no chart, and it will be screenshotted.

**4. One muted line on the trade sheet**, under the existing cap validation:
`next year: +$13.5M · through 2029-30`. **Display only.** It must be visually
distinct from the validation message above it, because that one blocks and this
one does not — see the rules.

No new icons. No new colours.

## The rules

- **"Next season" is `league.Season + 1`**, computed from the four-digit prefix.
  In the replay that is 2026-27; in production next October it will be 2027-28.
- **Enforcement: none.** This figure never blocks a trade. The cap rule the league
  actually wrote is a single-season rule, and refusing a trade on a constraint
  nobody agreed to would be the app inventing a league rule. If Nick wants it
  enforced later it is one call inside the existing shared validator, but it is
  not this feature's decision to make.
- **Accepted-but-unexecuted trades are included**, for exactly the reason
  `vTeamCommitments` exists: a GM could otherwise accept two long contracts in one
  morning, each looking fine on its own. Reuse the same union of open spots plus
  accepted deltas the trade validator already takes; do not build a second one.
- **A player traded away leaves the commitment immediately.** Unlike banked
  points, this is a forward-looking statement about the roster you will have, so
  it correctly follows the roster rather than the history.
- **The Équipe slot is excluded**, because it costs nothing against the cap by
  rule (scoring-model §1) and the two gauges must agree about what a cap is.
- **Draft picks are excluded** — no salary, no roster spot, same as
  `vTeamCommitments`.
- **The 16 active NHL players with no salary on file anywhere** land in the
  unsigned bucket at `DefaultCapHit`, and their count travels beside the total in
  the same way `vStandings.UnknownContracts` already does. Do not invent a second
  convention for reporting the same uncertainty.
- **Coverage thins with distance.** A 2029-30 row exists only for long deals, and
  that is correct rather than a data gap: "no row" for a far year genuinely means
  "not committed". But it means a gauge for N+3 would be near-empty and
  meaningless, which is why there is exactly one extra gauge and not four.
- **Nothing in the app can resolve an expiring contract.** There is no free
  agency, no draft, no re-sign. This feature is a thermometer, not a valve, and
  the UI must not imply otherwise — no button, no "extend", no countdown.
- **Week 1 of a brand-new league**: works immediately and needs no history at
  all. This is the only idea in the folder that is fully useful the hour a league
  is created.
- **Banking**: reads no scored rows and writes none. It cannot touch a
  `RosterAssignment`, a `FantasyPoints` or a `FinalizedUtc`.
- **Sim clock**: the roster it reads is the roster as of the simulated day, like
  every other roster query. Contracts are not date-bounded — a 2026-27 cap hit is
  a fact about a document, not about a day.
- **No authentication**: rosters and salaries are already public to the league,
  so this exposes nothing new. It should be visible for *every* team, not just
  your own — knowing a rival is capped out next year is the whole trade angle.

## What it costs

No migration. **A query in `Queries.cs`, not a view** — and that is a deliberate
call rather than an oversight. `vStandings` and `vTeamCommitments` are views
because several callers sum them and two callers re-deriving the same arithmetic
is how two screens start disagreeing; here exactly one screen consumes the
figure, and a view that has to do string arithmetic on a season code to find
"next year" is harder to read than the query it would save. Fold the result into
the team payload the Team screen already fetches, and add the two-field
next-season delta to the trade-validation response the sheet already receives —
no new endpoint, no new spinner on either screen. Frontend: a second gauge, a
grouped expandable list, one warning line, one line on the trade sheet. The pure
logic worth mocking-free tests is the three-bucket classifier, and it should be
tested specifically on the case that separates never-signed from expiring,
because that is the one a reasonable implementation gets wrong.

## What I rejected

- **Enforcing the future cap on trades.** It would refuse trades that are legal
  under the league's actual written rule. Show it, do not enforce it, until Nick
  says otherwise — and if he does say otherwise, that is a rules change that
  belongs in `scoring-model.md` §9, not in a feature file.
- **Reading `PlayerContracts.YearsRemaining`.** It is an import-time snapshot and
  it drifts the instant a season turns. The presence of a season row cannot.
- **Charging `DefaultCapHit` to expiring players next year.** It converts "you
  lose him" into "he costs a million", which is a false statement about the one
  thing this feature exists to say.
- **A multi-year chart, N+1 through N+4.** Fully computable and it is four bars a
  die-hard reads once. All of the dread lives in the season he can still act on,
  and CapWages coverage falls off a cliff beyond two years anyway, so bars three
  and four would be mostly empty and mostly wrong.
- **Guessing UFA versus RFA.** We do not store expiry status and CapWages carries
  it only in prose we deliberately do not parse. Putting a false claim about a
  real player's contractual rights on screen is worse than saying nothing, and the
  three buckets already carry every distinction we can honestly make.
- **A "cap health" score out of 100.** Compresses three honest numbers and a
  position warning into one arbitrary one, and nobody argues with a 74.
- **Cost per banked point as a companion metric on the same gauge.** Genuinely
  good and genuinely a different feature: this one is about a season that has not
  happened, that one is about a season that has, and putting the two on one gauge
  would make a GM read a forward-looking number as a verdict on his past. It
  deserves its own file, not a corner of this one.
- **A separate "Contracts" screen.** The nav is full at four and a cap figure is
  a fact about a roster, which is what the Team screen is for. Same reasoning
  that retired the Roster screen.
