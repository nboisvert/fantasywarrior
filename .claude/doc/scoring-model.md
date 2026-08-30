# Scoring model — reference

> The reference for every scoring rule. If the code and this document disagree, one of them is a bug.
> Read it before changing anything about scoring, lineups, roster spots or periods.

## In one sentence

Every week a GM activates a subset of his roster. Only **active** players score, and their points are **banked permanently**.

## 1. The entities

| Entity | Table | What it is |
|---|---|---|
| **Period** | `Periods` (global) | A scoring week. Monday→Sunday on the NHL Eastern game date. ~28 a season. |
| **RosterSpot** | `RosterSpots` | The membership of a **player or a franchise** on a team, from day X to day Y. Never deleted, only closed. Also carries `ProtectionStatus`, outside scoring — §11. |
| **RosterAssignment** | `RosterAssignments` | What a spot produced in one week, and whether it was active. **One row per (spot, week)** — the only grain stored. |
| **Team** | `Teams` | Carries **no running total**. Every total is a view (`vStandings`, `vTeamPeriodScores`, `vRosterSpotTotals`). |

**Central invariant**: `Score = FinalizedScore + LivePoints`, true **by construction** — `vStandings` derives `LivePoints` by subtracting the other two over the same rows, instead of keeping three hand-maintained fields.

Points reset each season because a **filter** moves, never because a row is deleted — see [data-model.md](data-model.md) for the two views that carry it.

### The Équipe slot (`T`)

A `RosterSpot` holds **a player or an NHL franchise** — position group `T`, at most one per team, guaranteed by a filtered unique index. It is an ordinary spot otherwise: one `RosterAssignment` per week, points that bank, tradeable. Three differences, all consequences of "only one per team":

- **Never benched.** One franchise, one seat, so there is no decision to make: no active/bench control, and `LineupRules.LegalActiveSet` keeps it active whatever is submitted.
- **Its stats come from `Games`**, not the player game log — `FranchiseResults.For`, the only thing it does not share with a player. A game with no `LastPeriodType` is not final; reading the score alone would call it a 0-0 regulation loss for both sides.
- **Costs nothing against the cap and is not a player**: `vStandings` excludes `PositionGroup = 'T'` from `CapTotal`, `PlayerCount` and `UnknownContracts`, and a trade moves it only **against another franchise**.

It reports **no `gamesPlayed`** — the slot announces a record, not a workload. Deliberate: `RosterGamesPlayed`, the denominator behind every points-per-game figure in the app, stays about players.

## 2. The three levels of aggregation

```
lineup.results[spotId].points        what one player produced this week
        ↓ sum of the actives
lineup.activePoints                  what the team scored this week
        ↓ banked at the end of the week
team.finalizedScore                  the closed weeks, immutable
        ↓ + the week in progress
team.score                           what the standings show
```

Alongside, `rosterSpot.activePoints` accumulates what a player earned for **this** team — the PTS column on the Team screen.

## 3. The weekly cycle

| Moment | What happens |
|---|---|
| **Monday 00:00 ET** | The week starts and **the lineup locks** (`Periods.LockUtc`). No further change. |
| Monday → Sunday | The nightly job recomputes the current week **from zero** each night. Nothing accumulates. |
| Each night | The **next** week's lineups are created by carry-forward if they do not exist. Never rewritten. |
| **End of Sunday + 1 grace day** | The week is **banked**: its points join `finalizedScore` and never move again. |

The **grace day** exists because the NHL corrects boxscores after the fact. Banking the same night would freeze what was known then and lose the next morning's correction silently. Banking re-scores the week one last time before freezing it, which is what actually picks those corrections up.

### Trades are not in this cycle

**Accepting a trade executes it** (`TradeExecution`), dated to the **following Monday** (`TradeSchedule.NextPeriodStart`): the outgoing spot gets an `EndDate` on the Sunday, the incoming spot a `StartDate` on the Monday, and that week's lineups follow — the leaver loses his row, the arrival gets one, inactive. Null past the last week of the season: no boundary is left to land on, and inventing one would open spots into a season with no weeks to score them.

A trade always takes effect on a week boundary, and a player never has two owners on the same day. Acceptance is what stamps the date, and that is what makes the lineup screen honest: the picker sets next week, so it must offer the roster the GM will actually have.

## 4. The rules

### Locking
Week N's lineup must be submitted **before** week N starts — the only cheat-proof option short of daily lineups: otherwise a GM could activate a player after he scored four goals on Monday.

**Consequence to accept**: a player acquired mid-week sits until the next Monday.

### Forgotten lineup
The previous week's lineup is **carried forward automatically**, minus players who left the roster, then **topped up** with the best available at each position (`LineupRules.CarryForward` → `AutoFill`). A GM on vacation is not punished — in a pool between friends that would drain the standings of meaning.

The row carries `SetBy = "auto"` so the UI can flag it. The carry-forward is **written** by `WeekAheadJob` each night and **never rewrites an existing row** — that is the whole rule, and what makes the nightly job safe to replay.

### Slots
Configurable by the commissioner, per position; Les Mordus' own numbers are in [mordus.md](mordus.md). This is the **only rule enforced at lineup submission**. Fielding **fewer** than the maximum is allowed — you simply score less. Fielding **more** is refused.

`LineupRules.LegalActiveSet` forces a possibly-illegal set legal deterministically, needed because an illegal lineup can arrive with nobody cheating — a commissioner shrinking the slots mid-season is enough. It drops the most recently activated player first: he is the one who made it illegal.

### Transactions
**Everything takes effect at the period rollover**, trades included. A roster spot therefore never opens mid-week, which removes a whole class of edge cases.

**A roster spot may start in the future.** That is the property everything else follows from: "never closed" and "held today" are different questions, and they diverge for exactly the two spots a trade creates.

| Question | Predicate (`RosterWindow`) | Used for |
|---|---|---|
| Held today | `OwnedOn`: `Start <= today && (End == null \|\| End >= today)` | the displayed roster, the **cap** |
| Owns that week | `OwnsPeriod`: `Start <= end && (End == null \|\| End >= start)` | the lineup, the scoring pass |
| **Committed** | `Committed`: `End == null` | what a trade is validated against |
| *engaged* (the badge) | `Engaged`: held today **and** `End != null` | "leaving soon" on screen |
| *arriving* | `Arriving`: `Start > today` | acquired, not here yet |

The old `EndDate IS NULL` filter did not become wrong — it changed meaning. It now names the committed roster.

### Banked points
Once a week is banked its points belong permanently to the team that fielded the player. **A trade cannot move history.** That is what allowed the entire compensation system (`Adjustment`) to be deleted: there is nothing left to compensate.

**Corollary**: changing the scale mid-season does not restate the past. The total becomes a mix of two scales — defensible, but to be accepted. The way out is to un-bank and replay ([deployment.md](deployment.md)).

> **No `recompute` job exists** — the live error message from `PATCH /api/leagues/{code}/rules` tells you to run one anyway. See [deployment.md](deployment.md) for the jobs that do exist.

### Playoffs
**Excluded.** `GameType == GameType.RegularSeason` (2) applies everywhere — `PeriodInitJob`, `PeriodRollupJob`, `vPlayerSeasonStats`, the API reads. A rule, not an accident.

### Dead weeks
A week with no game (Olympic break, All-Star) still exists and scores zero. `Period.GameCount` lets the UI say "pause" rather than show an unexplained 0. **The 2025-26 season has two** (9–22 February 2026, Milan-Cortina).

## 5. The formula

```
a player's points for a week = Σ (stat × scale value)
```

The scale is a **key→value map** over stat names (`StatKeys`), not a fixed list, so a commissioner can score blocked shots, hits or even games played **with no schema change**.

Les Mordus' own values live in [mordus.md](mordus.md) — that file is the only place they are written down, so a scale change is a one-file change.

The three team keys (`teamWins`, `teamLosses`, `teamOtLosses`) are **distinct** from the goalie ones. They happen to be worth the same here, and that is a coincidence: "my goalie won" and "my franchise won" are two different events on the same night, and a league wanting to price them differently has no way to say so if they share a key. They travel through `extraPointValues` like any other stat — no special-casing.

The five historical values live in `pointValues`, every other stat in `extraPointValues`; `RuleConfig.ScoringScale()` merges the two into the only form the engine consumes. An unknown key is **rejected by the API**, not absorbed: it would score zero forever and look like a calculation bug rather than a typo.

The free-agent leaderboard (`FreeAgentRanking`) ranks under the **league's own scale**, not raw NHL points — that is what lets a goalie's wins compete with a skater's goals on one list. It is a display only; there is no add/drop.

## 6. The scoring window

Three things restrict what a roster spot owns of a week, and all three matter:

1. **The spot may have opened or closed mid-week** — a player traded on Thursday keeps Monday-to-Wednesday for his old team.
2. **`lastStatDate` clamps the end** — scoring a day whose boxscores are not synced yet would bank a zero for it and never revisit it.
3. **A spot opening after the last synced day owns nothing** — `null`, not an empty range.

That is `StatWindow.Intersect`, the load-bearing function of the whole model.

## 7. Why the calendar is global

A week is a property of the NHL schedule, not of the pool. Sharing the boundaries across every league lets the nightly job fetch a week's game lines in **one query per date range**, serving all leagues from the same result set. Per-league calendars would bring back one query per league — the technical reason this is not negotiable.

The Eastern game date is the anchor: a West Coast game starting Sunday 22:30 ET and ending Monday 01:15 ET carries the Sunday date and belongs to Sunday's week. Comparing wall-clock timestamps would misfile exactly those games.

## 8. Properties not to break

- **Idempotence.** The current week is recomputed from zero, never accumulated. Banking only touches rows where `IsFinalized = 0` and stamps `Periods.FinalizedUtc`; a finalized row is never re-scored. Re-running `nightly` any number of times is a no-op.
- **Lineup provenance.** The GM writes `RosterAssignment.IsActive` and a `TeamPeriodLineups` row in his name; the job writes stats and points. The job reads that attribution to tell a real choice from its own auto-fill — without it, it would overwrite the GM's decisions.
- **Immutable periods.** Moving a boundary after the fact would restate points already owned. `period-init` may only append, never rewrite.
- **Transactional submission.** The full lineup is validated then written in one transaction, so two tabs cannot produce an illegal roster. **Trap**: `EnableRetryOnFailure` is on, so a manual transaction must be wrapped in `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` or it throws.

## 9. Commissioner settings

| Setting | Where | Enforced? |
|---|---|---|
| Point values (5 fixed + extras) | `ruleConfig.pointValues` / `.extraPointValues` | yes, at scoring |
| Active slots per position | `ruleConfig.topCount` | **yes, at lineup submission** |
| Roster size min/max | `ruleConfig.rosterSize` | **yes, on trades** (proposal and acceptance) |
| Salary cap | `league.capAmount` | **yes, on trades** (proposal and acceptance) |
| Cost of a player with no contract | `ruleConfig.defaultCapHit` | **yes**, in `vStandings` (today's *and* committed columns) and in trade validation |
| Équipe slot value | `ruleConfig.extraPointValues` (`teamWins`/`teamLosses`/`teamOtLosses`) | yes, at scoring |
| Draft picks per team per year | `ruleConfig.draftRounds` | one per round, generated by `draft-picks-init` |

**A player with no contract costs $1M by default**, per league — 0 restores the old free-carry behaviour. "No contract" is not a data hole to fill: it is the permanent, ordinary state of an unsigned free agent and of a drafted prospect who has not signed, and a keeper pool has many. Counting them at $0 let a GM stockpile them free and understated every total. `vStandings` therefore also exposes `UnknownContracts` — once an assumed salary is folded into the total nothing distinguishes it from a real one, and a figure half measured and half conventional has to say so.

**The cap is enforced against the *committed* figures, not the standings.** An accepted trade is irreversible; validating against today's roster would let a GM accept a $9M contract in the morning and bust the cap in the afternoon, each trade looking legal in isolation. Both figures come out of the **same view**: `CapTotal`/`PlayerCount` filter spots held today, `EngagedCapTotal`/`EngagedPlayerCount` those remaining once every trade lands. Same aggregation, one differing filter — they cannot drift apart. Same logic for assets: a player or a pick already moving in an accepted trade cannot be re-offered; *pending* offers lock nothing.

What is still **not** enforced: nothing stops a roster going out of bounds by some other path — there simply is no other path today (no free-agent add/drop).

All of this is set through the API — `PATCH /api/leagues/{code}/rules`, commissioner only — and the **League rules** panel in the app.

> **`capAmount` is not editable through that PATCH.** It is set at league creation or by `seed-mordus --cap`.

## 10. Operations

Commands — `period-init`, `nightly --backfill-from`, un-banking a week, moving a week's lock — live in [deployment.md](deployment.md).

## 11. The off-season

Entirely outside scoring: a protection changes no lineup, no points and no cap. It appears here only because `ProtectionStatus` lives on a `RosterSpot`, and this document must be read before touching spots.

The protection phase, the steal and rookie/free-agent draft segments, and the draft room are in [offseason.md](offseason.md).
