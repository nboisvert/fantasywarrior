# Les Mordus — the league's settings

The pool Fantasy Warrior is built for first. **This file is the single source for
its numbers**; every other doc links here rather than restating them. Rosters
come from `Classement Mordus pool a vie saison 3 — PoolExpert.com`, extracted to
[`data/mordus-rosters.json`](../../data/mordus-rosters.json) and materialised by
`seed-mordus` ([deployment.md](deployment.md)).

## Identity

Join code `TKW6UR` (drawn at random on every re-seed), season `20252026`,
**14 GMs**, commissioner `nick`, **418 roster spots** — 404 players plus one NHL
franchise each. `LeagueSeasons` says season **3**, confirmed by the source PDF's
title. Usernames are the GM's first name, disambiguated by a surname initial on
a collision (`jonathan` / `jonathanr`).

## Scoring scale

| Stat | Pts | | Stat | Pts |
|---|---|---|---|---|
| Goal | 1 | | Franchise win (`teamWins`) | 2 |
| Assist | 1 | | Franchise OT loss (`teamOtLosses`) | 1 |
| Goalie win | **2** | | Franchise regulation loss (`teamLosses`) | 0 |
| Goalie OT loss | 1 | | Shutout | 0 |

The franchise keys are deliberately separate from the goalie's — priced the same
here by coincidence, but "my goalie won" and "my franchise won" are different
events a league must be able to pay apart. The franchise total is read off the
`Games` table, never the players' game log (`FranchiseResults.For`).

## Format

**Active lineup 9 F + 4 D + 1 G**, plus the Équipe slot. The bench has no fixed
size (observed reserves run 7 to 20) and active ↔ reserve swaps every week.
**Roster 23 min, 35 max**, cap **$134M**, a contractless player counting $1M
(`League.DefaultCapHit`), **3 rookie draft rounds** a year.

Rules are applied through the commissioner-only rules panel
(`PATCH /api/leagues/{joinCode}/rules`); the cap is not on that endpoint and is
set at league creation or by `seed-mordus --cap`. Jobs and endpoints in
[deployment.md](deployment.md).

## Off-season rules

The pool is keeper: rosters carry over, **points reset to zero each season**, and
the pool has counted its own seasons for years — so there is no lifetime total to
model despite the report's "pool à vie" title. Between two seasons runs a draft
whose first two rounds are steal rounds. Mechanics live in
[offseason.md](offseason.md); the numbers are the league's:

| Rule | Value | Where |
|---|---|---|
| Protectable players per GM | 9 | `League.ProtectionSlots` |
| Steal rounds (so 2 steals per team) | 2 | `League.StealRounds` |
| Maximum losses per team | 2 | `League.MaxLossesPerTeam` |
| Auto-protected, goalie | ≤ 50 career NHL games | `ProtectionRules` |
| Auto-protected, skater | ≤ 100 career NHL games | `ProtectionRules` |
| Auto-protection costs a slot | No — free | |
| Unclaimed exposed players | Stay on their team | |

> ⚠️ **Decided, not entered.** All three columns are `NULL` on the Mordus
> `Leagues` row: `seed-mordus` writes the cap, roster bounds, active slots and
> `DraftRounds = 3`, nothing else, and no migration fills them in. **Les Mordus
> cannot run a real off-season in this state**, for two different reasons on the
> two paths:
>
> - `POST /protections/autofill` reads `ProtectionSlots` and refuses without it —
>   "Set the league's protection slots first, in the rules panel"
>   (`DraftEndpoints.cs:191`). No protection can be recorded at all.
> - `POST /draft/open` **never reads `ProtectionSlots`.** It gates on
>   `DraftRounds > 0` and on the pick count equalling teams × `DraftRounds`
>   (`DraftEndpoints.cs:350`), both of which Les Mordus can satisfy. It opens —
>   and then `StealRounds` null falls through `?? 0`, so the draft is all-rookie
>   with no steal segment and `MaxLossesPerTeam` null means uncapped losses.
>
> The rules panel writes `ProtectionSlots` only; **`StealRounds` and
> `MaxLossesPerTeam` have no writer in the UI or the API at all**, the one path
> today being `clone-league --steal-rounds / --max-losses`, which sets them on a
> copy and never touches the live league.

Two thresholds rather than one because a goalie plays about half his club's
games: measured at the skaters' bar he would stay untouchable twice as long. With
9 slots on an average roster of 29 and auto-protection not counted, a GM still
exposes a good half of his depth — the league's number, not a comfort setting.

## The Équipe slot (`T`)

The PDF gives every participant an `E` line holding his own NHL franchise, at $0
— it appears exactly 14 times. Each GM therefore owns one franchise for life, and
it scores. How a franchise slot is modelled, and why the club you *are* can
diverge from the club you *own*, is in [data-model.md](data-model.md); what it
pays is in the scale above.

## PoolExpert → Fantasy Warrior vocabulary

Useful when reading the source PDF.

| PoolExpert | Fantasy Warrior |
|---|---|
| Participant | `User` + `Team` — one `Team` per participant per league |
| His NHL franchise, the `E` line | `Team.FranchiseAbbrev` **and** a `T`-group `RosterSpot` (above) |
| `T` column (blank / `D` / `G`) | Ignored on import — position comes from `Players`, which is authoritative |
| Block above "JOUEURS DE RÉSERVE" | Active players — the week's `Lineup` (`activeSpotIds`) |
| "JOUEURS DE RÉSERVE" | Benched — same `RosterSpot`, simply absent from `activeSpotIds`; the bench is not a separate entity |
| `PSal` | `Player.CapHit` — the PDF is in millions (`9.50`), `CapHit` in dollars |
| `PPts`, `PPP`, `PJ`, `B`, `P`, `1/7/30` | Not imported; stats come from `PlayerGameStats` |

## Names and non-NHL players

Names the import cannot match are resolved by `player-resolve` from
[`data/unresolved-players.txt`](../../data/unresolved-players.txt) — command in
[deployment.md](deployment.md), matching rules in
[integrations.md](integrations.md). **A GM may dress non-NHL players**: they get a
normal roster assignment with every stat at zero, which is why two teams score
very little in a replay that carries week 1's lineup forward.
