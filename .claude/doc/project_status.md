# Fantasy Warrior — Project Status

> **MUST be read at the start of every session and kept updated along the way.**
> Last updated: 2026-07-22 (by Macklin Softwarini)

## Current state

**DONE (2026-07-22 evening): GM Dashboard redesign**
- [x] Backend: `GET /api/leagues/{id}/activity?limit=` — recent add/drop events (assignments-derived), enriched with player name/position/team name; `Assignment.closedUtc` added so drop events are orderable.
- [x] Frontend IA rework (built by a React Exposito subagent, reviewed and integrated): bottom nav is now 4 tabs — **Dashboard** (new default: hero header, rank/score/cap stat chips, top-4 mini standings, top-8 scorers of my roster tappable to PlayerCard, relative-time activity feed) · Standings · Roster · **Settings** (create/join league forms + my-leagues switch list + commissioner RulesPanel + logout — moved out of the main flow). `LeagueGate` trimmed to a pure picker, shown only when the user has 2+ leagues and no remembered one, or via the topbar league-switch. 0-league users land straight on Settings; exactly 1 league auto-selects. `fw-league` in localStorage still drives "last opened league" persistence.
- Verified: build clean, lint clean, landing-logic traced against real accounts (freshgm=0 leagues→Settings, testa=1 league→auto Dashboard, nick=2 leagues→picker).
- [x] **Density pass (2026-07-22 night)**: Nick found the header (league name + season) ate ~1/4 of the screen; hero collapsed from a ~193px stacked card to a ~106px single-strip header + slim stat-chip row, using PlayerCard's tight-grid vocabulary as the density reference (briefed with ui-ux-pro-max's "Data-Dense Dashboard" numbers: 8-12px padding, ~56-64px header). Built by a React Exposito subagent, scope-checked (only Dashboard.tsx + the dashboard CSS block) and integrated. 44px touch targets preserved on tappable rows.

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
