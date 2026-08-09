# Deployment & Operations Runbook

> Everything needed to run Fantasy Warrior live.
> Last updated: 2026-08-09 — refreshed the test count (`/doc-clean`); the
> SignalR/one-replica architecture below is unchanged since 2026-08-03.

## Architecture (live)

| Piece | Where | How it deploys |
|---|---|---|
| Frontend (React/Vite) | GitHub Pages — https://nboisvert.github.io/fantasywarrior/ | Auto on every push to `main` touching `frontend/**` (`frontend-deploy.yml`) |
| API (.NET 10 minimal API) | **Azure Container Apps** — app `fantasy-warrior-api`, environment `fantasy-warrior-env`, same region as the database, scales to zero | Auto on push to `main` touching `backend/**` (`api-deploy.yml`); image published to ghcr.io |
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

## The Container App runs exactly one replica (2026-08-03)

`api-deploy.yml` sets `--min-replicas 0 --max-replicas 1`. The ceiling is a
**correctness** requirement since the API started hosting a SignalR hub, not a
budget one. Two things in it are per-process:

- the hub's connection groups — a GM on replica A never receives a message
  pushed from replica B;
- `PresenceRegistry`, the in-memory map of who is connected — each replica would
  report the half of the league it doesn't hold as offline.

Both fail intermittently and only under load, which is the worst kind to chase.
Raising the ceiling needs a **backplane** (Azure SignalR Service or Redis) *and*
a shared presence store — not a bigger number.

### The awake-time budget

A WebSocket is a request that never ends, so **the app cannot scale to zero
while anyone is connected**. The free grant is 180 000 vCPU-s + 360 000 GiB-s a
month, and the replica is the default 0.5 vCPU / 1 GiB (no `--cpu`/`--memory` in
the workflow) — so the grant buys roughly **100 hours of an awake replica per
month**, past which it is pay-as-you-go, on the order of $30-35/month if it
never sleeps. Confirm against the Azure pricing calculator before assuming those
figures.

Billing is **per replica-second, not per connection**: five GMs connected at
once cost what one does. The budget is wall-clock *union* time where at least
one person is active, not the sum of their sessions.

What keeps it inside the grant is the client, in
`frontend/src/live/LiveProvider.tsx`: connect when a league loads and the tab is
visible, drop 60 s after the tab is hidden, stop immediately on `pagehide`.
**The hidden tab is the whole lever** — a tab forgotten on a second monitor is
what would otherwise hold the container up all night for nobody.

There is deliberately **no idle timeout and no activity tracking** (2026-08-03).
An earlier version dropped the socket after three minutes without a
pointerdown/keydown/scroll. It bought little on top of `visibilitychange` and
cost a lot: listeners on scroll, a "connected" flag that was a poor proxy for
"in the app", and a retry path that could hammer `/hubs/live/negotiate` once per
scroll event whenever the API was cold. Reconnection after a mid-session drop is
`withAutomaticReconnect()`'s job, not ours.

**If the awake hours ever look wrong, read that file first** — it is the only
thing standing between this feature and a monthly bill.

## Azure SQL configuration

**Public network access must be enabled.** The server shipped with *Deny Public
Network Access = Yes*, which blocks everything — including Cloud Run and GitHub
Actions — behind a misleading error: *"Database is not currently available.
Please retry the connection later."* If you ever see that, check this first.

Portal → SQL server `fantasywarrior` → Security → Networking:
- Public access: **Selected networks**
- Firewall rules: one per fixed IP that needs in (Nick's dev machine), plus
  `containerapp-N` rules the deploy workflow maintains automatically
- **"Allow Azure services and resources to access this server"** — on. It covers
  GitHub-hosted runners, which are Azure VMs. It does **not** cover Container
  Apps outbound traffic: the first deploy was refused by name, *"Client with IP
  address '20.200.119.174' is not allowed to access the server"*.

**The Container App's outbound IPs are allowed automatically.** A Container Apps
environment has *stable* outbound addresses — the one thing Cloud Run could not
offer, and the reason the API is here at all — so `api-deploy.yml` reads them
off the app and writes the matching firewall rules on every deploy. Nothing to
maintain by hand, and it self-heals if Azure ever moves them.

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
| Variable | `AZURE_RESOURCE_GROUP` | `fw` — the resource group holding the SQL server and the Container App |
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
dotnet test FantasyWarrior.slnx                                       # ~353 tests
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
dotnet run --project backend/FantasyWarrior.Jobs -- player-resolve            # before seeding — see below
dotnet run --project backend/FantasyWarrior.Jobs -- draft-sync
dotnet run --project backend/FantasyWarrior.Jobs -- career-sync
dotnet run --project backend/FantasyWarrior.Jobs -- capwages-sync --resolve-unmatched   # ~15 min
dotnet run --project backend/FantasyWarrior.Jobs -- seed-mordus
dotnet run --project backend/FantasyWarrior.Jobs -- sim-clock --set 2025-10-04 --season 20252026
```

### `player-resolve` — the players `player-sync` cannot see

`player-sync` reads two endpoints per team, the season roster and the prospect
list. A player can be on neither and still be someone a GM owns: an unsigned
free agent is on no roster, and a fresh draftee his club has not listed yet is
on no prospect list. `seed-mordus` refuses to write a league whose roster file
names a player it cannot find, so this has to run **before** seeding.

```powershell
dotnet run --project backend/FantasyWarrior.Jobs -- player-resolve [--file data/unresolved-players.txt] [--dry-run]
```

It reads one name per line (`#` comments allowed), queries the NHL search
endpoint by **surname**, and writes only what is unambiguous — anything with
no match or several is printed at the end for a human. Always `--dry-run`
first and read that list.

Names are kept spelled the way the source wrote them, on purpose: the matcher
is what absorbs Zack for Zachary and Sandin Pellikka for Sandin-Pellikka, and
correcting the input by hand would hide a regression in it.

Nothing here is scraped and no third-party source is involved — it is the same
official NHL API as everything else. EliteProspects was considered and is not
needed: every player in this situation still has an NHL id.

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
  rather than reporting a silent zero. Superseded by a more detailed diagnosis
  (client fingerprinting, deliberately not chased) — see project_status.md's
  Open items.
- **Cloud Run could not reach Azure SQL** (2026-08-02) — it has no stable
  outbound IP, and pinning one needs Cloud NAT at about $32/month. That is why
  the API moved to Azure Container Apps rather than why a firewall rule was
  added.
- **`Replacement index 1 out of range for positional args tuple`** during a
  Container Apps deploy (2026-08-02) — a bug in the `containerapp` extension's
  own error formatting, hit while it tries to auto-register a resource provider
  and cannot. It hides the real error. The cause is an unregistered
  `Microsoft.App` / `Microsoft.OperationalInsights`, and the deploy service
  principal is scoped to one resource group so it has no right to register a
  subscription-level provider. Fix once, in Cloud Shell, as an owner:
  ```bash
  az provider register --namespace Microsoft.App --wait
  az provider register --namespace Microsoft.OperationalInsights --wait
  ```
  `api-deploy.yml` warns about this up front and prints those two commands,
  but never blocks on it: reading provider state is itself a subscription-level
  call, so a resource-group-scoped principal cannot tell "not registered" apart
  from "not allowed to look".
- **The API answers the TLS handshake and then hangs forever** (2026-08-02) —
  ingress up, no healthy replica behind it. It meant the container was
  crash-looping, because the connection string was resolved at
  service-registration time and threw before the web host existed. Fixed by
  resolving it lazily; `/health` now answers whatever the database is doing, and
  `/health/db` reports the real reason separately. That endpoint is the first
  thing to hit when the app looks wrong.
- **`Client with IP address 'x.x.x.x' is not allowed to access the server`**
  (2026-08-02) — the Container App's outbound IP was not in the firewall, and
  "Allow Azure services" does not cover it. The deploy workflow now derives and
  writes those rules itself. Azure SQL takes up to five minutes to apply a new
  rule, so retry before assuming it failed.
- **The API hangs at TLS again, hours after a green deploy** (2026-08-02) —
  same symptom as the entry above, different cause, and the second one is
  nastier because the deploy that caused it passed.

  System events name it: `ImagePullUnauthorized`, then
  `Container ... terminated with ... reason 'ImagePullFailure'`.

  The workflow used to pass `--registry-password ${{ secrets.GITHUB_TOKEN }}`.
  That token **dies when the run ends**. A Container App with stored registry
  credentials *always* uses them and never falls back to an anonymous pull, so
  once the token expired the pull returned 401 **even though the ghcr package
  is public**. The deploy stayed green because the replica already had the
  image; the app only died at the next wake-up from `min-replicas 0`.

  Fixed by removing registry credentials entirely — the package is public, so
  the pull is anonymous. If it ever goes private, use a PAT with
  `read:packages`, never `GITHUB_TOKEN`. To repair an app still carrying the
  old credential:
  ```bash
  az containerapp registry remove -n fantasy-warrior-api -g fw --server ghcr.io
  az containerapp revision restart -n fantasy-warrior-api -g fw --revision <rev>
  ```
  `api-deploy.yml` now does the `registry remove` on every deploy, and its last
  step **asserts** `/health` returns 200 instead of merely printing the revision
  state — that reporting-without-asserting is what let this ship green.

  Two diagnostic lessons, both of which cost time here:
  - `--type console` shows what the app wrote; **`--type system` shows what
    Container Apps did to it.** A failed pull writes nothing to console, so an
    empty console log is a signal, not a dead end. Reach for system first.
  - `az containerapp logs show` reporting *"Successfully connected to
    container"* does **not** mean the image is good. A replica stuck in
    `ImagePullBackOff` still has a replica object with no container inside it.
- **A stale local `dotnet run` locks the build output** — kill the
  `FantasyWarrior.Api` or `FantasyWarrior.Jobs` process and rebuild.
