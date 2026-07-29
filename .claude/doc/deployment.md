# Deployment & Operations Runbook

> Everything needed to run Fantasy Warrior live. Written 2026-07-22 during the
> first production setup. Keep updated when infra changes.

## Architecture (live)

| Piece | Where | How it deploys |
|---|---|---|
| Frontend (React/Vite) | GitHub Pages — https://nboisvert.github.io/fantasywarrior/ | Auto on every push to `main` touching `frontend/**` (`frontend-deploy.yml`) |
| API (.NET 10 minimal API) | Google Cloud Run — **https://fantasy-warrior-api-197228637471.northamerica-northeast1.run.app** — service `fantasy-warrior-api`, region `northamerica-northeast1`, scales to zero (first deploy 2026-07-22) | Manual trigger: Actions → "Deploy API to Cloud Run" → Run workflow (`api-deploy.yml`) |
| Database | Firestore (Native), project `fantasywarriordb`, region `northamerica-northeast1` | — |
| Nightly data jobs | GitHub Actions cron 09:30 UTC (`daily-jobs.yml`): stats-sync → score-calc → process-trades → player-sync → draft-sync → news-sync | Auto; manual backfill via Run workflow with from/to inputs |
| News sync (standalone) | GitHub Actions (`news-sync.yml`), manual trigger only — runs just `news-sync`, with optional feed-URL override inputs, so it can be tested/rerun without touching stats/scoring/trades | Actions → "News sync" → Run workflow |
| Auth | TEMPORARY username-only (API trusts client). Firebase Auth (Google + email/password) planned. | — |

## GCP one-time setup (done 2026-07-22 by Nick)

1. **Billing** enabled on project `fantasywarriordb` (same project as Firebase). Expected cost: $0 at pool scale.
2. **APIs enabled**: Cloud Run Admin API, Artifact Registry API.
3. **Artifact Registry repo** created manually: name `fantasy-warrior`, format Docker, region `northamerica-northeast1`.
   ⚠️ Must exist BEFORE the deploy workflow runs — the `github-deploy` SA can push but cannot create repos (Writer role). Error otherwise: `Repository "fantasy-warrior" not found`.
4. **Service accounts** (both in project `fantasywarriordb`):
   - `github-deploy@…` — roles: Cloud Run Admin, Artifact Registry Writer, Service Account User. Used by `api-deploy.yml`.
   - `firebase-adminsdk-fbsvc@…` — Firestore admin access. Used by nightly jobs and local dev.
   Keys are JSON files; the two files look alike — `client_email` is what distinguishes them.

## GitHub repo configuration (Settings → Secrets and variables → Actions)

| Kind | Name | Value |
|---|---|---|
| Secret | `GCP_SA_KEY` | Full JSON key of `github-deploy` SA |
| Secret | `FIREBASE_SA_KEY` | Full JSON key of `firebase-adminsdk` SA (activates nightly cron) |
| Variable | `GCP_PROJECT_ID` | `fantasywarriordb` |
| Variable | `API_URL` | Cloud Run service URL (e.g. `https://fantasy-warrior-api-….a.run.app`) — injected as `VITE_API_URL` into the Pages build. Frontend falls back to `http://localhost:5099` when empty. |

GitHub Pages: Settings → Pages → Source = **GitHub Actions** (done).

## Local dev

- Secrets dir (never in git): `C:\Nick\secrets\fantasywarriordb-sa.json`.
- API: `ASPNETCORE_URLS=http://localhost:5099 GOOGLE_APPLICATION_CREDENTIALS=<key> FIRESTORE_PROJECT_ID=fantasywarriordb dotnet run --project backend/FantasyWarrior.Api --no-launch-profile`
- Frontend: `cd frontend && npm run dev` → http://localhost:5173
- Jobs: same env vars, `dotnet run --project backend/FantasyWarrior.Jobs -- <job>`; jobs: `player-sync`, `draft-sync` (backfills NHL draft round/pick/year/team, one HTTP call per player not yet checked), `salary-import --file x.csv`, `stats-sync [--from A --to B]`, `score-calc [--league id]`, `news-sync [--rotowire-url <url>] [--fantasysp-url <url>]` (Rotowire + FantasySP RSS → global `news` collection; default feed URLs are best-effort — override the flags if a source's feed location changes), `league-init-assignments`, `stats-check`, `player-check`.
- Solution file is `FantasyWarrior.slnx` (new .NET 10 format).

## Changing things later

- **New API version live**: push to main, then Actions → "Deploy API to Cloud Run" → Run workflow. (Deliberately manual for now; make it auto by adding a push trigger on `backend/**` in `api-deploy.yml` when ready.)
- **Missed cron days**: Actions → "Daily NHL data sync" → Run workflow with `from`/`to` dates (YYYY-MM-DD) to backfill.
- **Rotate a key**: create new key on the SA in GCP console → replace the GitHub secret → delete old key. For local dev, replace the file in `C:\Nick\secrets\`.

## Troubleshooting log

- **`Repository "fantasy-warrior" not found` on Build and push** (2026-07-22): Artifact Registry repo missing; the "Ensure repo exists" step can't create it with Writer role (`|| true` masks it). Fix: create the repo manually (see above).
- **Pages deploy failure right after enabling**: Pages source must be set to GitHub Actions in repo settings first.
- **firebase-tools emulator requires Java 21+**: local machine has Java 17 → use `npx firebase-tools@13` (we currently develop against live Firestore instead).
- **`news-sync` default feed URLs confirmed wrong** (2026-07-28→29): first live run (via a manually-triggered `daily-jobs.yml`, since `news-sync.yml` didn't exist yet) logged `rotowire: 0 items` / `fantasysp: 0 items` — both default URLs in `Jobs/Program.cs` are bad. `RssNewsClient` now logs the HTTP status/content-type (or parse failure, or "0 &lt;item&gt; found") per feed on a miss, so the next run's log will say *why* — check it, find the real feed URL/path, and either pass it via `--rotowire-url`/`--fantasysp-url` (or the `news-sync.yml` workflow's input fields) or update the defaults once confirmed. Use `news-sync.yml` (Actions → "News sync" → Run workflow) to iterate on this without re-running the full nightly chain.
