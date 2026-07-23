# Fantasy Warrior — Project Status

> **MUST be read at the start of every session and kept updated along the way.**
> Last updated: 2026-07-22 (by Macklin Softwarini)

## Current state

**Salary estimation (2026-07-22 night)** — Nick confirmed the NHL API genuinely has no contract/salary fields anywhere (verified live: grepped the player-landing and roster endpoints for salary/contract/capHit/aav, zero hits — PuckPedia really is the only source, and it's paid/private). Approved a stopgap: **`estimate-salaries`** job scales a plausible cap hit between $3M-$14M across the top 200 real scorers (goals+assists, from `playerSeasonStats`) and flats everyone else at $1M; every write is tagged `capHitSource: "estimated"` so it's never confused with real contract data later. Ran once (1371 players updated), then `score-calc` re-run for Shemalz Pool. **Real bulk salary data is still the long-term TODO** (CSV from Nick or a PuckPedia subscription) — this is a clearly-labeled placeholder.
- Side finding while verifying post-estimate scores: `nick`'s team had 7 players instead of 8. Traced via the `assignments`/activity history: a genuine **drop** event for Zach Werenski exists, ~2 minutes after the initial draft (2026-07-23T02:23 UTC) — not a bug in any job (none of `estimate-salaries`/`set-league-cap`/`score-calc` touch roster composition). Likely dropped via the app's remove button during phone-testing, before Roster became read-only later that session. Werenski is untouched in the `players` collection; just ask if he should be re-added to `nick`'s roster.

**Salary investigation (2026-07-22 night)** — Nick reported salaries "seem lost". Verified: **not a bug**. The 2 test players imported early this session (Danault $5.5M, Matheson $4.875M) still have intact `capHit` — the merge-field protection in `PlayerSyncJob` works correctly across every subsequent player-sync. What actually happened: a **real bulk salary import was never done** — only that original 3-player smoke-test CSV ever ran. Shemalz Pool's real roster (McDavid, Crosby, etc.) never had salary data to begin with (this was already an open TODO). PuckPedia's data API is private/paid (confirmed earlier — see Phase 1 notes); **still need Nick to provide a CSV or decide on a paid source** before cap totals mean anything for the current league.
- [x] Added `set-league-cap --league <id> --amount <dollars>` job (reusable; 0 clears the cap). Set Shemalz Pool (`pfGBZO5rjcjIYgyxMLLn`) to **$100,000,000**.
- [x] **Roster/Stats refinements (2026-07-23)**: unified salary format to `$9.6M` everywhere (removed the "9.6M$" variant that briefly existed on Roster's rows — one compact-money helper now, not two); Stats header reworked so team name + points total share one row/baseline (`.stats-header-top`), with the adjustment pill moved to its own line below; replaced the sticky "gros footer" totals card with a plain `<tfoot>` row per grid (Skaters/Goalies), summed per column (GAA/SV% recomputed from summed raw components, not averaged); added **PTS/M** (points per game), **Salary**, and **$/PTS** (cost per point) columns to the Skaters grid, **Salary** to the Goalies grid. Verified against live data (McDavid: 138 pts / 82 GP → 1.68 PTS/M, $14M cap → ~$101K/PTS).
- [x] **Roster split into Roster + Stats screens (2026-07-23)**: new backend endpoint `GET /api/leagues/{id}/teams/{username}/season-stats` (cache-first via new `PlayerTotalsSource.FetchWithCacheAsync`, ~1 read/rostered player instead of re-scanning game lines) backs a new **5th bottom-nav tab, "Stats"** (Dashboard/Standings/Roster/Stats/Settings). Roster is now team/cap-composition only (score/points/adjustment header content removed); player rows are exactly two lines (F/D/G pill + name on line 1, team + compact salary "9.6M$" on line 2). Stats screen owns everything performance-related: a compact score/adjustment header (same content, visually smaller than Roster's old one) + two data grids (Skaters: GP/G/A/PTS/+‑/PIM/SOG; Goalies: GP/W/OTL/SO/GAA/SV%, sticky player-name column, horizontal scroll contained to the grid) + a sticky footer summing every column (GAA/SV% recomputed from summed raw components, not averaged-of-averages), positioned above the fixed bottom nav via `bottom: calc(var(--nav-height) + safe-area)`. Built by a React Exposito subagent (scope for once legitimately included `App.tsx` for the new tab), scope-checked, integrated, build clean.
- [x] **Roster header/list redesign**: score is now the headline (big Russo One number), signed adjustment total shown as a pill next to it (hidden when zero), cap gauge rebuilt as two `PlayerCard`-style tiles (Available/Used, flips to danger red + "Over budget" past 100%) above the existing `.cap-track` bar, reads calmly at today's real 0%-used state (no salary data imported yet). Player rows: position collapsed to a color-coded F/D/G pill (`--ice-bright`/`--silver`/`--gold` tokens, never color-only — letter always shown), stray "—"/"·" artifacts from null capHit removed (cap-hit sub-text now omitted entirely when null, no punctuation-join separators). Built by a React Exposito subagent, scope-checked (Roster.tsx + Roster-only CSS), integrated, build clean.

**Data reset (2026-07-22 night, redone with all-star draft)**: added `wipe-pools` job (deletes `users` + `leagues` incl. subcollections `teams`/`assignments`/`adjustments`; `players`/`games`/`playerGameStats` untouched — reusable for clearing test data) and `seed-allstars` job — drafts the top synced performers (by fantasy points, season stats already in Firestore) via a **snake draft** across 9 fixed GMs for roster balance, **writes team docs directly (no adjustment ledger)** so full-season production counts as if each player had always been on that team (no compensating transaction, per Nick's "assume all players belonged to their team for the whole season").
- Superseded same night by a **full-season redo**: backfilled the **entire 2025-26 regular season** (confirmed boundaries via the NHL API: 2025-10-07 → 2026-04-16, gameType 2; preseason/playoffs excluded) — **1342 games, 51264 player lines** now in `playerGameStats`.
- Added **`playerSeasonStats`**, a consolidated per-season cache (collection, doc id `{season}_{playerId}`) — see the new CLAUDE.md note under "advanced stats service". Populated as a write-through side effect of `PlayerTotalsSource.FetchAsync` (nightly scoring) and the `/api/players/{id}` endpoint (already fetches the lines for recentGames, so caching is free); bulk-seeded by `seed-allstars`'s full-season scan (1038 players cached in one pass). Deliberately **not** refreshed via scheduled full-season rescans — a season is ~49k lines, and Firestore's free tier caps at 50k reads/day.
- `seed-allstars` reworked to draft **by position**: `--forwards 4 --defense 3 --goalies 1` (options, defaults shown), separate snake-drafted pools per `PositionGroup` instead of an undifferentiated skater/goalie split.
- Re-seeded **"Shemalz Pool"** (id `pfGBZO5rjcjIYgyxMLLn`, season 20252026) from the full-season pool (615F/325D/98G available) — real recognizable stars: McDavid, MacKinnon, Draisaitl, Crosby, Kucherov, Pastrnak, Kaprizov, Makar, Hughes, Rantanen, Vasilevskiy... Season-long scores realistic and balanced: 577-667 pts across 9 teams, `adjustmentsTotal: 0` everywhere (full-season ownership assumption holds).
- `PlayerRawTotals` (Core/Scoring) extended with the full stat line (plusMinus, pim, shots, hits, blockedShots, goalsAgainst, saves, shotsAgainst) so there's one aggregation shape shared by scoring, the cache, and the player-card API (removed a duplicate ad hoc aggregation in the API endpoint).
- Earlier league ids (`PZPF8iZDlzW1y4TmHKTw`/6-per-team/20262027 and `cacsxkwrzp76djvtMZLz`/6-per-team) are gone (wiped); `pfGBZO5rjcjIYgyxMLLn` is current. `seed-allstars` remains a reusable command for future re-seeds.

**DONE (2026-07-22 evening): GM Dashboard redesign**
- [x] Backend: `GET /api/leagues/{id}/activity?limit=` — recent add/drop events (assignments-derived), enriched with player name/position/team name; `Assignment.closedUtc` added so drop events are orderable.
- [x] Frontend IA rework (built by a React Exposito subagent, reviewed and integrated): bottom nav is now 4 tabs — **Dashboard** (new default: hero header, rank/score/cap stat chips, top-4 mini standings, top-8 scorers of my roster tappable to PlayerCard, relative-time activity feed) · Standings · Roster · **Settings** (create/join league forms + my-leagues switch list + commissioner RulesPanel + logout — moved out of the main flow). `LeagueGate` trimmed to a pure picker, shown only when the user has 2+ leagues and no remembered one, or via the topbar league-switch. 0-league users land straight on Settings; exactly 1 league auto-selects. `fw-league` in localStorage still drives "last opened league" persistence.
- Verified: build clean, lint clean, landing-logic traced against real accounts (freshgm=0 leagues→Settings, testa=1 league→auto Dashboard, nick=2 leagues→picker).
- [x] **Density pass (2026-07-22 night)**: Nick found the header (league name + season) ate ~1/4 of the screen; hero collapsed from a ~193px stacked card to a ~106px single-strip header + slim stat-chip row, using PlayerCard's tight-grid vocabulary as the density reference (briefed with ui-ux-pro-max's "Data-Dense Dashboard" numbers: 8-12px padding, ~56-64px header). Built by a React Exposito subagent, scope-checked (only Dashboard.tsx + the dashboard CSS block) and integrated. 44px touch targets preserved on tappable rows.
- [x] **Round 5 (2026-07-22 night) — full restart, mobile-native**: Nick called out that rounds 3-4 had drifted into a "desktop BI dashboard crammed onto a phone" feel and asked for a from-scratch rebuild. New design: single vertical stack of full-width cards (no side-by-side grid), styled with **PlayerCard's own spacing/type-scale reused verbatim** (`.pc-tiles`/`.pc-tile` classes, not re-tightened). Content: an "At a glance" card with 4 tiles (Players count, Cap Space remaining in compact `$X.XM` format, Rank as ordinal, Points as the accented stat) + a "points behind leader" caption (or "Leading the pool"); a "Top scorers" card (5 players, reusing Roster's `.player-row` markup) tappable to `PlayerCard`. The activity ticker is fully removed (not hidden) — deferred, to return later. All prior dense-grid/2-column/ticker CSS deleted (CSS bundle shrank 19.7kB→15.8kB). Same night, **Roster became read-only** (add/drop UI removed — rosters are season-long per the current model; backend endpoints untouched for future use). Built by a React Exposito subagent (briefed against ui-ux-pro-max's touch-spacing guidance as a corrective), scope-checked, integrated, build clean.
- [x] **Round 4 (2026-07-22 night) — phone-tested refinements**: (a) 2-column layout now actually fits real phones — collapse breakpoint dropped 480px→320px, padding/gaps tightened throughout (`.dash-grid` gap, `.mini-row` unified to `0.26rem 0.5rem` for both tables, `.cap-meter.dash-cap-meter` given explicit tight padding); (b) activity ticker reworked into a **3-row sliding vertical carousel** (doubled-list windowing, `translateY` slide every 3.6s, seamless wraparound rewind, static/no-timer fallback under `prefers-reduced-motion` or <3 entries, visually-hidden `aria-live` announcer for just the newly-entered row); (c) scorer rows gain an **F/D/G position pill** (`.msr-pos-pill`, cloned from the old `.counted-badge` pill look) next to the name, second column now just the NHL team abbreviation; (d) alignment pass unifying padding/columns between the standings and scorers mini-tables. Built by a React Exposito subagent, scope-checked (Dashboard.tsx + dashboard CSS only), integrated, build clean.
- [x] **Round 3 (2026-07-22 night) — full redesign**: 2-column top section (CSS Grid, named areas, collapses to 1 column below 480px) — left: ultra-compact top-5 standings (rank/team/score, single line, no handle) with a **"gap to my rank" indicator**: if the viewer's team isn't in the top 5, show ranks 1-4, a bold ice-cyan divider, then the viewer's true rank as a 5th row; right column: a dedicated **Cap Space card** reusing Roster's `.cap-meter` gauge verbatim, stacked above a **Recent Activity ticker** (single entry at a time, auto-loops every 3.6s, pauses on hover/focus, respects `prefers-reduced-motion` via a live `matchMedia` hook, `aria-live="polite"`, fixed-height viewport to avoid layout jump). Below both columns, full width: top-7 scorers pushed to a third density pass (row padding 0.45rem→0.28rem, still 44px tap targets via the button's own min-height). Removed the old 3-chip stat row entirely (rank/score/cap now conveyed by the standings block + cap card, no duplication). Built by a React Exposito subagent (ui-ux-pro-max consulted for gauge/ticker/2-col dashboard + WCAG 2.2.2 pause requirement), scope-checked (Dashboard.tsx + dashboard CSS only), integrated, build clean.
- [x] **Round 2 (2026-07-22 night)**: (a) new **no-duplicate-destination** rule added to CLAUDE.md — removed the "View full standings" link (Standings already in bottom nav); (b) mini-standings/top-scorers rebuilt as dense grid "mini-table" rows (no headshots, no position pills, no per-row cards — a single bordered container with hairline-separated grid rows); (c) league name + season moved out of the Dashboard body entirely, now shown subtly in the topbar's league-switch button (name + small muted season, hidden below 400px). Also dropped the "TOP" counted-badge text globally (Standings/Roster/Dashboard) — confusing without context; kept the quieter `.player-row.counted` border tint for future reuse. Built by a React Exposito subagent, scope-checked, integrated.

**🚀 LIVE since 2026-07-22** — full stack in production, all free-tier:
- App: https://nboisvert.github.io/fantasywarrior/ (GitHub Pages, auto-deploy on push)
- API: https://fantasy-warrior-api-197228637471.northamerica-northeast1.run.app (Cloud Run, manual deploy workflow)
- Nightly cron active (stats → scores → rosters, 09:30 UTC)
- E2E verified in prod: login, standings, player card. See `.claude/doc/deployment.md` for the full runbook.

**Priority change (Nick, 2026-07-22): minimal UI + league schema FIRST, auth bypassed** — login is username-only for now (API trusts the client; Firebase Auth token verification will replace it later). Stats service (Phase 2) pushed after the minimal UI.

**League schema + minimal UI: DONE (first cut)**

- [x] Firestore schema: `users/{username}`, `leagues/{id}` (name, season, capAmount, commissioner, memberUsernames), `leagues/{id}/teams/{username}` (one team per user per league, `playerIds` roster)
- [x] API endpoints: login, my-leagues, create/join league (league id = invite code), league detail (teams + rosters + capTotal), player search (in-memory PlayerCache, 10-min TTL), roster add/remove with one-owner-per-player-per-league conflict check
- [x] React UI (mobile-first, EN): username login → league gate (create/join/pick) → app shell with **fixed bottom nav (Standings default + Roster)** and league switcher in top bar
- [x] UI design system (2026-07-22, via ui-ux-pro-max): dark "night arena" theme — bg `#0a0e1a`, ice-cyan accents `#38bdf8/#22d3ee`, glass cards, Russo One (display) + Chakra Petch (body), podium colors for ranks, cap meter with overage state, Lucide SVG icons. Tokens in `frontend/src/index.css`; screens in `frontend/src/screens/`
- [x] End-to-end verified locally against prod Firestore: full flow incl. ownership conflict and cap totals. Test data left in prod: league "Buddies Pool Test" (users nick, marc)
- [ ] Roster structure rules (nb of F/D/G) — needs Nick's pool rule sheet
- [ ] Real auth (Firebase Auth) — deliberately deferred

**Local dev**: API → `ASPNETCORE_URLS=http://localhost:5099 dotnet run --project backend/FantasyWarrior.Api --no-launch-profile` (+ `GOOGLE_APPLICATION_CREDENTIALS`, `FIRESTORE_PROJECT_ID=fantasywarriordb`); frontend → `npm run dev` in `frontend/` (API base URL via `VITE_API_URL`, defaults to localhost:5099).

**Phase 4 — Scoring engine: DONE** (plan: `.claude/doc/plans/phase4-scoring-engine.md`)

- [x] `RuleConfig` per league (commissioner-editable): point values (defaults G/A/OTL=1, W=2, SO=0) + top X per position group (F/D/G, null = all)
- [x] `ScoringEngine` (Core, pure, 7 unit tests): player points, top-X selection with deterministic ties, transaction adjustment
- [x] **Transaction invariance**: roster add/drop creates `adjustments` ledger entries so team score never moves at transaction time; `score = rawTopXScore + adjustmentsTotal`
- [x] `assignments` history per league: playerId, team, from/to (ET dates), **source** (initial | free_agency | trade | draft) + sourceRefId; opened/closed by roster endpoints; `league-init-assignments` migration ran
- [x] `score-calc` job (stateless recompute) wired in daily-jobs.yml between stats-sync and player-sync; `countsInScore` persisted as `countedPlayerIds` per team
- [x] API: PATCH /api/leagues/{id}/rules (commissioner), GET /api/players/{id} (bio + season totals + last 10 games, isHome), league detail exposes score/points/counted/ruleConfig, teams sorted by score
- [x] UI: standings show real scores (+adj), expandable team rosters, TOP badge on counted players; roster shows player points + cap meter; RulesPanel (gear icon, commissioner only); **PlayerCard** bottom-sheet (bio, season tiles, last 10 games) built by React Exposito agent, wired on all player rows
- [x] E2E verified on 2025-26 data: league DJgrL5jEEMVSJekAZ3v5 (testa/testb) — top-X exclusion works, Marner↔Bratt trade left both scores unchanged, next-day sync moved only new production
- Note: `isHome` added to playerGameStats (Apr 1-5 re-synced). Goalie goals/assists not in NHL boxscore goalie group — not counted (rare, acceptable v1).

**Phase 2 — Stats service: DONE (cron pending secret)**

- [x] Firestore: `games/{gameId}` (scores, lastPeriodType REG/OT/SO) and `playerGameStats/{gameId_playerId}` (full skater + goalie lines, queried by `date`)
- [x] `StatsSyncJob`: `stats-sync [--date | --from/--to]` (default yesterday UTC), idempotent upserts, backfill-ready; derived flags: `shutout` (solo goalie, 0 GA), `otLoss` (decision O). `stats-check` for verification
- [x] Unit tests (TOI parse, shutout rules, date ranges) — 16 tests green
- [x] Verified on real 2025-26 data: 2026-04-01→04 synced (34 games, 1296 lines); spot-checked vs known results (Marner hat trick, Oettinger shutout, Kuemper OTL)
- [x] `daily-jobs.yml`: cron 09:30 UTC (stats-sync + player-sync) with manual backfill inputs
- [ ] **Nick: add GitHub secret `FIREBASE_SA_KEY`** (content of the service-account JSON) to activate the cron

**Phase 1 — Player service: IN PROGRESS**

- [x] `Player` model in Core (`players` collection, doc id = NHL player id)
- [x] `NhlApiClient` (api-web.nhle.com: standings, rosters per season with offseason fallback, prospects)
- [x] `PlayerSyncJob` — run: `dotnet run --project backend/FantasyWarrior.Jobs -- player-sync [--season YYYYYYYY]`; salary field (`capHit`) protected from sync overwrites via merge-fields
- [x] First prod sync done 2026-07-22: **1371 players** upserted into project `fantasywarriordb`; verified with `player-check` command
- [x] Salary import: `salary-import --file <csv>` (columns `nhlId?,firstName,lastName,teamAbbrev,capHit`; match by NHL id or normalized name + team tiebreak; unmatched rows reported). Verified end-to-end in prod incl. capHit surviving a full player-sync. **PuckPedia API is private/paid** (https://puckpedia.com/tools/data) → CSV is the retained path; Nick could still contact them.
- [ ] Real full salary dataset to import (waiting on a source/CSV from Nick)
- [ ] Daily cron workflow for player-sync (blocked on GitHub repo)

**Firebase (live)**: project `fantasywarriordb`, Firestore Native in northamerica-northeast1, production-mode rules. Service account key (never committed): `C:\Nick\secrets\fantasywarriordb-sa.json`. Jobs need env vars `GOOGLE_APPLICATION_CREDENTIALS` + `FIRESTORE_PROJECT_ID=fantasywarriordb`.
⚠️ `frontend/src/firebase.ts` still holds the web config of project `requinopen` (first attempt) — replace with the `fantasywarriordb` web app config (Nick to provide from console).

**Phase 0 — Foundations: DONE (local scope)** — GitHub repo/Pages/GCP linking deferred by Nick (work local first, link repo later).

- [x] Stack decided (see CLAUDE.md)
- [x] Full roadmap approved by Nick (plan of 2026-07-22)
- [x] CLAUDE.md updated (stack, AI team: React Exposito / Backend Mackinnon / Db Crosby)
- [x] project_status.md initialized (this file)
- [x] Monorepo scaffolding: `FantasyWarrior.slnx` (.NET 10: Api/Core/Jobs/Core.Tests), React+Vite+TS in `frontend/`, `.editorconfig`
- [x] API `/health` endpoint + Dockerfile (Cloud Run ready, port 8080); verified locally (build, tests, curl /health OK; frontend `npm run build` OK)
- [x] CI/CD workflows: `ci.yml` (build+test on PR/push), `frontend-deploy.yml` (GitHub Pages), `api-deploy.yml` (Cloud Run, **manual trigger until GCP secrets exist**)
- [ ] Firebase/GCP setup (manual steps by Nick — see "Waiting on Nick" below)
- [ ] Verification in prod: React page on GitHub Pages + API /health on Cloud Run (blocked on GitHub repo + GCP setup)

Note: .NET 10 SDK generates the new `.slnx` solution format — use `FantasyWarrior.slnx` in all dotnet CLI commands.

## Roadmap (approved 2026-07-22)

| Phase | Scope | Status |
|---|---|---|
| 0 | Foundations: repo scaffolding, Firebase/GCP setup, CI/CD | In progress |
| 1 | Player service: `players` collection, PlayerSync job (NHL API + PuckPedia salaries) | Todo |
| 2 | Stats service: `games` + `playerGameStats`, daily StatsSync job, backfill | Todo |
| 3 | Core domain: users, leagues, teams, multi-tenancy, auth middleware, security rules | Todo |
| 4 | Rules & scoring engine (configurable per league) + daily ScoreCalc job | Todo |
| 5 | Frontend MVP: login, standings, team/roster pages, commissioner UI | Todo |
| 🏁 | **MVP in prod for early October 2026 (NHL 2026-27 season)** | — |
| 6 | Transactions & free agency (post-MVP) | Todo |
| 7 | Interactive live draft (target: 2027-28 season) | Todo |

## Key decisions log

- **2026-07-22** — Stack: React/Vite/TS on GitHub Pages, Firestore, Firebase Auth (Google + email/password), .NET 10 minimal API on Cloud Run, .NET jobs via GitHub Actions cron. Constraint: hosting easy and free.
- **2026-07-22** — Scoring v1: points (goals, assists; goalie wins, shutouts, OT losses) + salary cap. Rules engine must be configurable per league for future configs.
- **2026-07-22** — First increment: season tracking (rosters entered manually by commissioner); draft happens outside the app for 2026-27, live draft targeted for 2027-28.
- **2026-07-22** — UI language: English only.

## Open items

- **Buddies' pool rule sheet** (exact point values, roster structure, cap amount) — needed for Phase 4. Ask Nick.
- **PuckPedia salaries access** (ToS / method; fallback: manual CSV import) — validate in Phase 1.

## Waiting on Nick (manual steps, Phase 0)

1. Create the Firebase project (console.firebase.google.com), enable **Firestore** and **Auth** (Google + Email/Password providers).
2. Enable GCP billing (credit card on file; expected bill: $0 at our scale) and enable **Cloud Run**.
3. Create a **service account** for the API/jobs, download its JSON key (will go into GitHub secrets — never committed).
4. Create the GitHub repository and push (repo is currently local-only, no remote).
