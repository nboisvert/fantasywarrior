# Test mode — replaying the 2025-26 season

> **The simulated date is not in this file.** It lives in the single-row
> `SimulationState` table, the only source of truth; no code reads this
> document. Run `sim-clock` before trusting any doc about where the replay is.
>
> Skill: [`testmode/SKILL.md`](../skills/testmode/SKILL.md) · scoring:
> [`scoring-model.md`](scoring-model.md) · jobs: [`deployment.md`](deployment.md).

## Why it exists

The weekly scoring model only proves itself over a whole season: the Monday
lock, carrying forward a lineup a GM forgot, landing trades at the rollover,
twenty-eight weeks of accumulation. The 2025-26 season is already in the
database, so the replay walks it day by day, treating each evening as the
nightly job would have.

**`stats-sync` is the only step skipped**: the game lines are already stored, so
re-fetching them from the NHL API would be waste. Everything else really runs —
season aggregates, weekly scoring, banking, lineup carry-forward, trades.

The simulated clock is the current day for the whole app, API included, and with
it scoring, banking, lineup locks, trade execution and season aggregates. Two
things stay deliberately on the real clock: news sync and its 30-day purge, a
real data pipeline, and `player-sync`, whose NHL rosters must stay the real ones.

## The cursor

`SimulationState.AsOfDate` is **the last game day whose results are known**. The
simulated day is the one after it, reproducing the real-world relationship
(`lastStatDate = today − 1`) exactly — which is why no scoring code carries a
special case for simulation (`FantasyWarrior.Data/SimulationClockService.cs`,
`FromAsOfDate`).

Consequence: for the simulated day to be the eve of the season, set the cursor
**two days** before the start of week 1. One day later and the app believes it
is Monday, past the midnight lock, and the opening lineup freezes before anyone
can enter it.

## Commands

| Need | Command |
|---|---|
| Where we are | `sim-clock` |
| Advance | `sim-advance --to 2025-11-23` |
| Start over | `wipe-pools`, then `seed-mordus` and `sim-clock --set 2025-10-04` |
| Back to real time | `sim-clock --off` |

`sim-advance` **stops at every week end it crosses**, so each week is really
scored and banked rather than skipped, and a trade lands at the boundary it was
accepted before rather than all at the end.

It is also exposed over HTTP, to advance without a PowerShell prompt:
`POST /api/testmode/advance?username=nick&to=2025-11-23&dryRun=false`. Reserved
to `nick` — 403 for any other `username`, because the simulation is global and
banks weeks for real, so a stray tap from another GM's phone is a different order
of mistake than a bad lineup edit. Not real auth (the app has none yet), just a
guard, and it goes away with the rest of test mode. The response carries the
job's console output in `output`; a run already in progress answers 409.

## Two things that surprise people

**The grace day.** A week is banked one day after it ends, to let late NHL
boxscore corrections through. Advancing to Sunday scores the week without banking
it; you have to reach Monday.

**There is no going back.** The simulation only moves forward — a cursor that
could move backwards would need every banked week un-banked in step with it, and
getting that wrong is silent. To replay a scenario, start over. **There is no
`sim-reset` job**; that name has never matched anything.
