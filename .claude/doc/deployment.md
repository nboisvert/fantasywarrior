# Deployment & Operations Runbook

> Everything needed to run Fantasy Warrior live.
> Last updated: 2026-08-02 — Azure SQL migration complete.

## Architecture (live)

| Piece | Where | How it deploys |
|---|---|---|
| Frontend (React/Vite) | GitHub Pages — https://nboisvert.github.io/fantasywarrior/ | Auto on every push to `main` touching `frontend/**` (`frontend-deploy.yml`) |
| API (.NET 10 minimal API) | **Azure Container Apps** — app `fantasy-warrior-api`, environment `fantasy-warrior-env`, region `canadacentral`, scales to zero | Auto on push to `main` touching `backend/**` (`api-deploy.yml`); image published to ghcr.io |
| **Database** | **Azure SQL** — server `fantasywarrior.database.windows.net`, database `fantasywarrior`, General Purpose Serverless (free tier) | Schema by `db-migrate`, never at app startup |
| Nightly data jobs | GitHub Actions cron 09:30 UTC (`daily-jobs.yml`): db-migrate → stats-sync → nightly → player-sync → draft-sync → news-sync | Auto; manual backfill via Run workflow with from/to |
| News sync (standalone) | GitHub Actions (`news-sync.yml`), manual only | Actions → "News sync" |
| Auth | TEMPORARY username-only (the API trusts the client). | — |

**The API and the database sit in the same Azure region** since 2026-08-02. The
API ran on Cloud Run, and moving it was not a preference: Cloud Run has no
stable outbound IP, so it could never be allowed through the Azure SQL firewall
without paying for Cloud NAT (about $32/month), which breaks the project's
"hosting stays free" rule. Co-locating also removed the cross-cloud round-trip
on every query and made the database's wake-up far less visible.

## Azure SQL configuration

**Public network access must be enabled.** The server shipped with *Deny Public
Network Access = Yes*, which blocks everything — including Cloud Run and GitHub
Actions — behind a misleading error: *"Database is not currently available.
Please retry the connection later."* If you ever see that, check this first.

Portal → SQL server `fantasywarrior` → Security → Networking:
- Public access: **Selected networks**
- Firewall rules: one per fixed IP that needs in (Nick's dev machine)
- **"Allow Azure services and resources to access this server"** — on. This is
  what lets the Container App connect, and GitHub-hosted runners too, since
  those are Azure VMs. No per-IP rule is needed for either.

`daily-jobs.yml` can instead open a per-run firewall rule for a runner outside
Azure; that path is skipped unless `AZURE_RESOURCE_GROUP` is set, which is not
the normal case.

### The serverless tier auto-pauses

After an idle hour the database pauses. The first connection then fails and the
resume takes roughly ten seconds; from fully cold it can be a couple of minutes.
`EnableRetryOnFailure(6, 20s)` plus a 60-second command timeout absorbs this.
The API being in the same region now means a user only pays the resume itself,
not a resume plus a cross-cloud round-trip — still worth watching on the first
visit of the day.

Free tier: 100,000 vCore-seconds/month (about 27 hours of compute) and 32 GB.
The season holds ~50,000 game lines and uses a fraction of the space; the
vCore budget is the one worth watching, and a full-season replay is the heaviest
thing that touches it.

## GitHub repo configuration (Settings → Secrets and variables → Actions)

| Kind | Name | Value |
|---|---|---|
| Secret | `AZURE_SQL_CONNECTION` | Full Azure SQL connection string |
| Secret | `AZURE_CREDENTIALS` | Service-principal JSON from `az ad sp create-for-rbac --sdk-auth`, used to deploy the Container App |
| Variable | `AZURE_RESOURCE_GROUP` | Resource group holding the SQL server and the Container App |
| Variable | `API_URL` | The Container App URL — injected as `VITE_API_URL` into the Pages build |

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
- **Cloud Run could not reach Azure SQL** (2026-08-02) — it has no stable
  outbound IP, and pinning one needs Cloud NAT at about $32/month. That is why
  the API moved to Azure Container Apps rather than why a firewall rule was
  added.
- **A stale local `dotnet run` locks the build output** — kill the
  `FantasyWarrior.Api` or `FantasyWarrior.Jobs` process and rebuild.
