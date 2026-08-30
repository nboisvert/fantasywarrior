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
(`cap.defaultCapHit`), **3 rookie draft rounds** a year.

Every one of these is a field of the league's rules document — what each means
and where it is enforced is in [league-rules.md](league-rules.md). They are set
through the commissioner-only rules panel (`PATCH /api/leagues/{joinCode}/rules`)
and written by `seed-mordus` when it builds the league. Jobs and endpoints in
[deployment.md](deployment.md).

## Off-season rules

The pool is keeper: rosters carry over, **points reset to zero each season**, and
the pool has counted its own seasons for years — so there is no lifetime total to
model despite the report's "pool à vie" title. Between two seasons runs a draft
whose first two rounds are steal rounds. Mechanics live in
[offseason.md](offseason.md); the numbers are the league's:

| Rule | Value | Where |
|---|---|---|
| Protectable players per GM | 9 | `protection.slots` |
| Steal rounds (so 2 steals per team) | 2 | `draft.steal.rounds` |
| Maximum losses per team | 2 | `draft.steal.maxLossesPerTeam` |
| Auto-protected, goalie | ≤ 50 career NHL games | `protection.auto.goalieMaxCareerGames` |
| Auto-protected, skater | ≤ 100 career NHL games | `protection.auto.skaterMaxCareerGames` |
| Auto-protection costs a slot | No — free | `protection.auto.enabled` |
| Unclaimed exposed players | Stay on their team | `protection.afterDraft` |

> All seven are fields of the league's rules document and all seven are on the
> rules panel, so an off-season can be configured entirely from the app.
> `draft/open` refuses a steal segment with no protection slots, and refuses one
> whose protection slate is empty: a half-configured off-season stops rather than
> running as an all-rookie draft with uncapped losses. Whether the live league's
> document *carries* these values is a status question — see
> [project_status.md](project_status.md).

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
