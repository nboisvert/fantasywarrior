# Fantasy Warrior — Project Status

> **MUST be read at the start of every session and kept updated along the way.**
> Last updated: 2026-07-22 (by Macklin Softwarini)

## Current state

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
