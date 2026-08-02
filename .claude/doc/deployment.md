# Deployment & Operations Runbook

> Everything needed to run Fantasy Warrior live.
> Last updated: 2026-08-02 — Azure SQL migration complete.

## Architecture (live)

| Piece | Where | How it deploys |
|---|---|---|
| Frontend (React/Vite) | GitHub Pages — https://nboisvert.github.io/fantasywarrior/ | Auto on every push to `main` touching `frontend/**` (`frontend-deploy.yml`) |
| API (.NET 10 minimal API) | Google Cloud Run — service `fantasy-warrior-api`, region `northamerica-northeast1`, scales to zero | Manual: Actions → "Deploy API to Cloud Run" (`api-deploy.yml`) |
| **Database** | **Azure SQL** — server `fantasywarrior.database.windows.net`, database `fantasywarrior`, General Purpose Serverless (free tier) | Schema by `db-migrate`, never at app startup |
| Nightly data jobs | GitHub Actions cron 09:30 UTC (`daily-jobs.yml`): db-migrate → stats-sync → nightly → player-sync → draft-sync → news-sync | Auto; manual backfill via Run workflow with from/to |
| News sync (standalone) | GitHub Actions (`news-sync.yml`), manual only | Actions → "News sync" |
| Auth | TEMPORARY username-only (the API trusts the client). | — |

**The API and the database are in different clouds.** Every query crosses the
public internet. It works, and the data layer is written to keep the round-trip
count low, but co-locating the API on Azure would remove both that latency and
the firewall dance below. See `sql-migration-plan.md`, "Décisions prises".

## Azure SQL configuration

**Public network access must be enabled.** The server shipped with *Deny Public
Network Access = Yes*, which blocks everything — including Cloud Run and GitHub
Actions — behind a misleading error: *"Database is not currently available.
Please retry the connection later."* If you ever see that, check this first.

Portal → SQL server `fantasywarrior` → Security → Networking:
- Public access: **Selected networks**
- Firewall rules: one per fixed IP that needs in (Nick's dev machine; Cloud
  Run's egress IP)
- **"Allow Azure services and resources to access this server"** — on. This does
  *not* cover Cloud Run, which is not an Azure service.

GitHub-hosted runners have dynamic IPs, so `daily-jobs.yml` opens a rule for the
run and deletes it afterwards rather than leaving the server open to 0.0.0.0.
That needs the `AZURE_CREDENTIALS` secret and the `AZURE_RESOURCE_GROUP`
variable; without them those steps are skipped.

### The serverless tier auto-pauses

After an idle hour the database pauses. The first connection then fails and the
resume takes roughly ten seconds; from fully cold it can be a couple of minutes.
`EnableRetryOnFailure(6, 20s)` plus a 60-second command timeout absorbs this for
jobs. **For a user request it is a bad first impression** and is the strongest
argument for moving the API to Azure.

Free tier: 100,000 vCore-seconds/month (about 27 hours of compute) and 32 GB.
The season holds ~50,000 game lines and uses a fraction of the space; the
vCore budget is the one worth watching, and a full-season replay is the heaviest
thing that touches it.

## GitHub repo configuration (Settings → Secrets and variables → Actions)

| Kind | Name | Value |
|---|---|---|
| Secret | `AZURE_SQL_CONNECTION` | Full Azure SQL connection string |
| Secret | `AZURE_CREDENTIALS` | Service-principal JSON with rights on the SQL server, for the firewall steps |
| Secret | `GCP_SA_KEY` | JSON key of the `github-deploy` SA (Cloud Run) |
| Variable | `AZURE_RESOURCE_GROUP` | Resource group holding the SQL server |
| Variable | `GCP_PROJECT_ID` | `fantasywarriordb` |
| Variable | `API_URL` | Cloud Run service URL — injected as `VITE_API_URL` into the Pages build |

⚠️ **The repository is public.** A credential committed to it is readable by
anyone, permanently, including from git history — rotating it would be the only
real fix. Real credentials live in `appsettings.Local.json`, which `.gitignore`
excludes; the committed `appsettings.json` holds a placeholder only.

## Local dev

Credentials: `backend/FantasyWarrior.{Jobs,Api}/appsettings.Local.json`, or the
`AZURE_SQL_CONNECTION` environment variable, which wins. Nothing else is needed.

```powershell
dotnet run --project backend/FantasyWarrior.Api --no-launch-profile   # :5099
cd frontend && npm run dev                                            # :5173
dotnet test FantasyWarrior.slnx                                       # 151 tests
```

Jobs: `dotnet run --project backend/FantasyWarrior.Jobs -- <job>`. See the
comment block at the top of `Jobs/Program.cs` for all of them.

Integration tests use **LocalDB** (`MSSQLLocalDB`), creating and dropping
`FantasyWarriorTests` each run — they never touch Azure. Set
`FW_TEST_SQL_CONNECTION` to point elsewhere; with no SQL Server at all they skip
cleanly rather than failing.

`dotnet-ef` is needed only to author migrations:
`dotnet tool install --global dotnet-ef --version 10.0.10`.

## Rebuilding the database from nothing

```powershell
dotnet run --project backend/FantasyWarrior.Jobs -- db-migrate
dotnet run --project backend/FantasyWarrior.Jobs -- player-sync --season 20252026
dotnet run --project backend/FantasyWarrior.Jobs -- stats-sync --from 2025-10-07 --to 2026-04-16  # ~10 min
dotnet run --project backend/FantasyWarrior.Jobs -- period-init --season 20252026
dotnet run --project backend/FantasyWarrior.Jobs -- capwages-sync
dotnet run --project backend/FantasyWarrior.Jobs -- seed-mordus
dotnet run --project backend/FantasyWarrior.Jobs -- sim-clock --set 2025-10-04 --season 20252026
```

`wipe-pools` clears leagues, teams, rosters and un-banks every week while
leaving players, contracts, games and game lines alone — that half costs hours
to rebuild and is identical for everyone.

## Troubleshooting log

- **"Database is not currently available. Please retry the connection later."**
  (2026-08-01) — usually *Deny Public Network Access*, not a paused database.
  Connect to `master` instead and the real error appears.
- **"The configured execution strategy does not support user-initiated
  transactions"** (2026-08-02) — `EnableRetryOnFailure` is on, so a manual
  transaction has to go through `db.Database.CreateExecutionStrategy()`. Not a
  formality: a retry must replay the whole transaction, not the surviving half.
- **A fresh league scores zero for weeks that already happened** (2026-08-02) —
  `wipe-pools` used to leave `Periods.FinalizedUtc` set, so those weeks could
  never be banked again. Fixed; the wipe now un-banks them.
- **FantasySP returns 403 Forbidden** (2026-08-02) — the site started blocking
  the scraper. Rotowire's two sources still work. `news-sync` logs the status
  rather than reporting a silent zero.
- **Cloud Run cannot reach Azure SQL** — its egress IP needs its own firewall
  rule; "Allow Azure services" does not cover it.
- **A stale local `dotnet run` locks the build output** — kill the
  `FantasyWarrior.Api` or `FantasyWarrior.Jobs` process and rebuild.
