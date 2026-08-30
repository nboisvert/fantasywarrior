# The off-season

Phases, protections, the steal draft and the rookie/free-agent draft — the whole
window between two seasons. Tables and indexes: [data-model.md](data-model.md).
Les Mordus' own numbers (slots, steal rounds, loss quota, auto-protection bars):
[mordus.md](mordus.md). Jobs and the SQL runbook: [deployment.md](deployment.md).

## 1. Three things are called "season"

The word covers three identifiers, and conflating them is what makes "are we
entering 2026 or 2027?" unanswerable.

| | What | Scope | Where it lives |
|---|---|---|---|
| **The NHL season** | `"20262027"` | Global — a fact about the NHL | A string on `Games`, `PlayerGameStats`, `PlayerContracts`, `PlayerCareerSeasonStats`, `Periods`, `SimulationState`, `Leagues` |
| **The league's season** | "Les Mordus, season 4" | Per league | A `LeagueSeasons` row |
| **The draft year** | `2026` | Per league | Derived — `Season.StartYear(s)` |

**The NHL season stays a value, and the `Seasons` table carries its dates.** The
string is the NHL's own identifier — the same argument as `Player.PlayerId` — so
it remains the key on every table, with no foreign key anywhere. Succession is a
pure function: `Core/Seasons/Season.cs` — validity, start/end year,
next/previous, the September rollover, the `"2026-27"` display form. `IsValid`
earns its place because the season is free text in the database, so
`"2025-2026"` would otherwise create a ghost season in silence.

What the string cannot carry is **when the season runs**, which is why the table
exists: the weekly calendar used to be derived from the games already imported,
so next season's weeks could not exist until the NHL published its schedule and
`stats-sync` had fetched it. `Seasons` holds the published dates; `Games` hold
what happened; `SeasonBounds.Resolve` reconciles them. See
[data-model.md](data-model.md).

**A draft is named for the calendar year it is held in, a season for the two
years it spans**, so `DraftPick.Year` is `Season.StartYear` of the season being
prepared: the 2026 draft stocks `"20262027"`.

`Leagues.Season` names the row the league is currently playing, by value — see
[data-model.md](data-model.md) for how the two tables are related and why.

## 2. The six phases

Each `LeagueSeasons` row walks its own lifecycle: prepared, played, closed.

```
Preparing ──> Protecting ──> Drafting ──> PreSeason ──> InSeason ──> Complete
```

| Phase | Open | Closed |
|---|---|---|
| `Preparing` | nothing | everything — the starting state |
| `Protecting` | the GM picks his protected players | **trades frozen** |
| `Drafting` | steals and rookie picks, one turn at a time | trades, protections |
| `PreSeason` | trades, repairing an out-of-bounds roster | lineups |
| `InSeason` | weekly lineups, trades, scoring | protections, the draft |
| `Complete` | nothing | everything — read-only forever |

`SeasonPhaseRules.Next` allows one step at a time and never backwards: skipping
`Protecting` straight to `PreSeason` would mean a draft that never happened,
silently.

**Exactly one row per league is anything but `Complete`: that is the current
season**, enforced by a filtered unique index. This settles who owns the
off-season — **its phases belong to the season being *prepared***, not the one
that just ended. In July, season 3 sits `Complete` with its champion written
while season 4 sits `Protecting`; the standings still show season 3, because
`Leagues.Season` still points there, and flip in one move on entry to `InSeason`.

**The trade freeze is not cosmetic.** A trade closes a roster spot and opens a
new one, and the new spot inherits no protection — a player his GM had just
protected would silently become stealable. `SeasonPhaseRules.CanTrade` refuses
`Protecting` and `Drafting` for that reason, and the check sits in
`TradeEndpoints.ValidateAgainstEngagedAsync`, the entry point shared by proposing
and accepting, so both paths refuse identically.

**`PreSeason` exists because a team can leave the draft out of bounds in either
direction** — under `RosterMin` (two players lost, one drafted back) or over
`RosterMax` (an already-full team that steals without ever shedding anyone). It
is the window to trade back into shape before lineups matter again.

Entering `InSeason` advances `Leagues.Season` and clears the protection slate;
entering `Complete` writes `ChampionTeamId`. Both are commissioner actions —
nothing advances a phase on its own, and **there is no way back**: the prepared
season has neither `Games` nor `Periods` until the NHL publishes its schedule, so
the standings would empty out. Reversing one is SQL, not a transition — written
in advance in [deployment.md](deployment.md).

## 3. Protections

Between seasons each GM shelters a limited number of players —
`League.ProtectionSlots`, one slot per player, on `RosterSpots.ProtectionStatus`.
Everyone else is exposed and can be stolen during the steal rounds; an exposed
player nobody claims stays put, having never moved. A protection is worth exactly
one off-season: `protection-reset` clears the slate on the way into `InSeason`.

**The slate is written by an autofill, and that is a default rather than a
choice.** `POST .../protections/autofill` (commissioner-only, `Protecting` only,
refused unless `ProtectionSlots` is set, `?preview=true` to see it without
writing) protects each roster's top scorers from the season just ended, ranked
**under the league's own scale** — on raw NHL points a goalie's season is zero and
no goalie would ever be protected — and bounded to the simulated day like every
other season total. `ProtectionAutofill.Choose` is the pure part, ties broken on
`PlayerId` so two runs pick the same men. The protection screen does not exist,
so **no GM can contradict the default**.

**Auto-protection is free and derived, never stored.** A player with too little
NHL experience — separate bars for goalies and skaters, since a goalie plays
roughly half his club's games — is out of reach without spending a slot, which is
what stops the pool becoming a prospect raid every summer. What is stored is the
measurement, `Players.CareerNhlGames`, written by `career-sync`; the verdict is
`ProtectionRules.IsAutoProtected`, so a threshold moves without rewriting a row.
A slot is only spent on someone the draft could actually take —
`ProtectionAutofill.NeedsASlot` skips anyone already untouchable, since a slot
burnt there would be wasted and would expose the veteran it should have covered.

**Three shelters, not two.** `ProtectionRules.KindOf` returns `ByGm`, `Auto`,
`Unknown` or `Exposed`, mirroring the untouchable branches of
`DraftPool.StealReason` in the same order — a cross test asserts the two cannot
diverge. `Unknown` is a player whose career total was never synced: `DraftPool`
refuses him on exactly that ground, so he is safe, but calling him
"auto-protected" would report a gap in our data as a rule of the pool. Likewise
**zero is not "we don't know"** — `CareerNhlGames` is null exactly when
`CareerStatsSyncedUtc` is, and the UI then shows nothing rather than stamping
`AUTO` on a veteran whose sync failed. An `Équipe` (`T`) slot holds a franchise,
which can only move against another franchise, so no draft can take one and no
slot is spent saying so.

**Every slate is public to the whole league.** `GET .../protections` and the
room's Protections pane show any team's slate, one at a time, and are not
phase-gated. Hiding them buys nothing — the steal pool gives them away by
omission, since a veteran missing from the available list is a veteran somebody
protected. An exposed player is the *absence* of a protection, so he is a count
and not a row: the screen lists the untouchables and says "13 exposed" beside.

## 4. The draft

The `Drafting` phase holds **two drafts run back to back in one room**, one turn
at a time.

| | Steal segment | Rookie / free-agent segment |
|---|---|---|
| Turns | rounds × teams, **derived** | the `DraftPicks` rows, **tradable** |
| Order | reverse standings, linear (not a snake) | `DraftPick.CurrentTeamId` |
| Pool | the *other* teams' exposed players | unrostered players |
| Consumes | nothing | one `DraftPick` |

**The order is frozen when the room opens and never re-read.**
`POST .../draft/open` is the only moment the standings are consulted: it writes
the reverse-standings position into `DraftPick.PickInRound` and moves the phase.
Every later request reads that frozen ordering, and the two segments read it
differently — the steal order is round 1's `PickInRound` taken by
**`OriginalTeamId`**, the team the pick was *given* to, while rookie turns follow
**`CurrentTeamId`**, the entitlement that actually changes hands. So trading a
first-rounder moves the rookie pick and leaves the steal turn where it was: it
was never attached to it. That asymmetry is what lets both segments share one
room and one turn engine without interfering. Re-reading the standings per
request would break the moment `Leagues.Season` advances, since `vStandings`
would then report the new season.

**There is no clock.** The GM on the clock picks whenever he gets to it and
everyone waits; nobody is skipped and nothing picks automatically. That is a
product decision, and it is also what keeps the whole draft inside request
handling — no `IHostedService`, no `BackgroundService`, no timer anywhere in the
backend, so the Container App still scales to zero between picks. "Whose turn is
it" reduces to a pure function of how many selections have been made.

**Steal turns are derived and not tradable, so they have no entitlement row.**
That is what makes `DraftSelections` necessary rather than convenient: with
nothing to claim, the unique index on `(LeagueSeasonId, OverallIndex)` is the
only thing stopping two GMs from taking turn 7. **The draft can never be derived
from `RosterSpots` carrying `StartReason = Draft` either** — `SeedMordusJob`
opened all 418 original Mordus spots with exactly that reason, so the log has to
be a table of its own.

**A turn can be passed** (`PlayerId` null). Teams × the loss quota can equal the
steal segment's turn count exactly, so a GM late in the order can genuinely face
an empty pool and would otherwise deadlock the draft. A passed rookie turn still
spends its entitlement.

**A player moves at most once per draft**, in either segment — a unique index on
`(LeagueSeasonId, PlayerId)`, and the same rule in `DraftPool` so the pool never
offers a row the database will then refuse.

**Neither `RosterMin` nor `RosterMax` is enforced on a selection.**
`DraftRules.ValidateSelection` delegates to `TradeRules.Validate` with both bounds
passed as null, deliberately: a selection only ever runs during `Drafting`,
`PreSeason` always follows, and a team already at `RosterMax` before its steal
turn could otherwise never take anyone, with no way to shed a player inside the
draft. **The salary cap still applies** — a different rule, `capAmount` passed
through unchanged. Trades keep enforcing both bounds; the loosening is the
draft's alone. The loss quota is a separate check, `DraftRules.ValidateLoss`,
applied to the team being robbed.

**The board shows every turn, made or not.** `DraftOrder.Remaining` walks the
turns still to come and the API concatenates them onto the selections made, so
"what just happened" and "what is coming" are one list read from either end.
`TurnsUntil` is an index into that same list rather than a second walk forward —
two walks that diverged would tell a GM "you pick in 3" and then not be his turn.
An unmade turn is a **projection**: a rookie pick traded mid-draft changes hands.
The steal half cannot move.

⚠️ **`Players.CareerNhlGames` is read live, never frozen at draft time.** No
games are played in the off-season, so it does not drift on its own; the exposure
is a draft held during a simulation, where `sim-advance` moves days quickly.
Bounding it would take a `Season <= <date>` filter on `PlayerCareerSeasonStats`
and a column on `LeagueSeasons`.

The gaps in this machinery — the missing protection screen, `PreSeason` trades,
the rollup ignoring phase — are tracked in
[project_status.md](project_status.md).
