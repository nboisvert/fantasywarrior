# Deployment & Operations

> How Fantasy Warrior ships, how its infrastructure is configured, every job and
> runbook, and the traps that have already cost a night. **This file owns jobs,
> commands and runbooks** — no other doc restates them.

## Architecture

| Piece | Where | How it gets there |
|---|---|---|
| Frontend (React/Vite) | GitHub Pages — https://nboisvert.github.io/fantasywarrior/ | `frontend-deploy.yml` |
| API (.NET 10 minimal API) | Azure Container Apps — app `fantasy-warrior-api`, environment `fantasy-warrior-env`, same region as the database, scales to zero | `api-deploy.yml`, image on ghcr.io |
| Database | Azure SQL — server `fantasywarrior.database.windows.net`, database `fantasywarrior`, General Purpose Serverless (free tier) | `db-migrate`, **never at app startup** |
| Nightly jobs | GitHub Actions cron 09:30 UTC | `daily-jobs.yml` |
| News sync (standalone) | GitHub Actions, manual only | `news-sync.yml` |
| Auth | TEMPORARY username-only — the API trusts the client | — |

**The API and the database sit in the same Azure region**, and that is not a
preference. On Cloud Run the API had no stable outbound IP, so it could never be
allowed through the Azure SQL firewall without paying for Cloud NAT
(~$32/month), which breaks the "hosting stays free" rule; co-locating also
removed a cross-cloud round-trip on every query. The image goes to ghcr.io rather
than Azure Container Registry because ghcr.io is free here and ACR Basic is not.

---

## Deploying

### What ships automatically

| Workflow | Trigger | Result |
|---|---|---|
| `frontend-deploy.yml` | push to `main` touching `frontend/**` (or the workflow itself) | `npm ci && npm run build` with `VITE_API_URL` = repo variable `API_URL`, then GitHub Pages |
| `api-deploy.yml` | push to `main` touching `backend/**` (or the workflow itself) | Docker build → ghcr.io tagged with the commit SHA → `az containerapp create`/`update` → SQL firewall rules → health assertion |

**Both also carry `workflow_dispatch`**, so either can be run by hand from the
Actions tab — pick the workflow, "Run workflow", `main`. That is how you
redeploy without a code change: after setting `API_URL` for the first time,
after rotating `AZURE_SQL_CONNECTION`, or to re-push an image.

`api-deploy.yml` is idempotent end to end. It creates the Container Apps
environment on the first run and no-ops after, creates the app if absent and
updates it otherwise, and re-derives the SQL firewall rules every time.

### What CI does, and what it does not

`ci.yml` runs on **every push to `main` and every pull request**: one job builds
`FantasyWarrior.slnx` in Release and runs `dotnet test`, another runs
`npm ci && npm run build` in `frontend/`. It has no `workflow_dispatch`.

**It gates nothing.** The deploy workflows are independent triggers on the same
push, not downstream of CI, and no required-check rule stands between them. A red
CI run and a green deploy of the same commit are both possible. CI tells you the
tree builds and the tests pass; it does not stop a broken commit reaching prod.

### What a deploy does NOT do

**Migrations never run at API startup.** `db-migrate` is deliberately a command,
not a startup hook — several instances could otherwise race into the same schema
change. It runs in exactly one automated place: the *Apply migrations* step,
first in `daily-jobs.yml`.

The ordering constraint follows: **a migration must be applied before the code
that needs it serves traffic.** Deployed code ahead of the schema is the failure
mode — `Invalid column name '…'` on whichever endpoint reads the new column,
which in practice means every screen, since they all load league detail first.
When a change carries a migration, apply it right after the deploy, or run
`daily-jobs.yml` from the Actions tab — it takes optional `from`/`to` date
inputs, which is the whole point of running it by hand: a backfill over a date
range rather than yesterday alone.

```powershell
dotnet run --project backend/FantasyWarrior.Jobs -- db-migrate --list   # what is pending
dotnet run --project backend/FantasyWarrior.Jobs -- db-migrate
```

### Verifying a deploy landed

`api-deploy.yml`'s last step **asserts**: it polls `/health` for up to 60 seconds
and fails the run if it never returns 200, printing the Container Apps *system*
log and the console log when it does. A green API deploy therefore means a
replica really answered.

- **`/health`** never touches the database — it measures the container being up.
- **`/health/db`** runs a real query and reports the database separately. It is
  the **first thing to hit when the app looks wrong**: it separates "the API is
  down" from "the serverless database is resuming".

The app's URL is the repository variable `API_URL`, or read it live with
`az containerapp show -n fantasy-warrior-api -g fw --query
"properties.configuration.ingress.fqdn" -o tsv`. A frontend talking to the wrong
API is a stale `API_URL` at build time, not a runtime setting.

### What is provisioned by hand

No workflow creates these; they exist because someone made them once.

| Thing | Notes |
|---|---|
| Resource group `fw` | Holds the SQL server and the Container App. Exposed to the workflows as the variable `AZURE_RESOURCE_GROUP`. |
| Azure SQL server + database `fantasywarrior` | General Purpose Serverless, free tier. `api-deploy.yml` reads the **region** off this server rather than hardcoding one, since co-location is the whole reason the API is in Azure. |
| SQL networking | Public network access on, "Selected networks", plus "Allow Azure services…" — see below. |
| Service principal behind `AZURE_CREDENTIALS` | `az ad sp create-for-rbac --sdk-auth`, scoped to the resource group `fw`. That scope is deliberate and has a visible consequence: it cannot register a subscription-level resource provider. |
| Provider registration | One-time, by a subscription **owner**, in Cloud Shell: `az provider register --namespace Microsoft.App --wait`, same for `Microsoft.OperationalInsights`. `api-deploy.yml` prints these as a warning but never blocks. |
| ghcr.io package visibility | The API image package is **public**, which is what lets the Container App pull it anonymously. Keep it that way — see the `ImagePullUnauthorized` trap. |
| GitHub Pages | Enabled on the repository with GitHub Actions as the source, and the `github-pages` environment `frontend-deploy.yml` deploys into. |

The Container Apps environment `fantasy-warrior-env` and the app itself are
**not** in this list: `api-deploy.yml` creates both if they are missing.

### Secrets and variables (Settings → Secrets and variables → Actions)

| Kind | Name | Value |
|---|---|---|
| Secret | `AZURE_SQL_CONNECTION` | Full Azure SQL connection string |
| Secret | `AZURE_CREDENTIALS` | Service-principal JSON, used to deploy and to open firewall rules |
| Variable | `AZURE_RESOURCE_GROUP` | `fw` |
| Variable | `API_URL` | The Container App URL — injected as `VITE_API_URL` into the Pages build |

`api-deploy.yml` checks all three up front and fails with a named list rather
than deep inside an `az` call.

⚠️ **The repository is public.** A credential committed to it is readable by
anyone, permanently, including from git history — rotating it would be the only
real fix. Real credentials live in `appsettings.Local.json`, and `.gitignore`
needs **both** patterns: `appsettings.*.Local.json` requires a middle segment and
does **not** match `appsettings.Local.json`, so the bare name must be listed on
its own line. The committed `appsettings.json` holds a placeholder only.

---

## Azure SQL configuration

**Public network access must be enabled.** The server shipped with *Deny Public
Network Access = Yes*, which blocks everything — GitHub Actions included —
behind a misleading error: *"Database is not currently available. Please retry
the connection later."*

Portal → SQL server `fantasywarrior` → Security → Networking:
- Public access: **Selected networks**
- Firewall rules: one per fixed IP that needs in (Nick's dev machine), plus the
  `containerapp-N` rules the deploy maintains
- **"Allow Azure services and resources to access this server"** — on. It covers
  GitHub-hosted runners, which are Azure VMs. It does **not** cover Container
  Apps outbound traffic.

**The Container App's outbound IPs are allowed automatically.** A Container Apps
environment has *stable* outbound addresses, so `api-deploy.yml` reads them off
the app and writes the matching rules on every deploy. Azure SQL takes up to five
minutes to apply a new rule. `daily-jobs.yml` instead opens and closes its own
per-run rule, guarded on `AZURE_RESOURCE_GROUP` being set — which it is (`fw`),
so that path runs every night.

**The serverless tier auto-pauses.** After an idle hour the database pauses; the
first connection then fails and the resume takes ~10 seconds, or a couple of
minutes from fully cold. `EnableRetryOnFailure(6, 20s)` plus a 60-second command
timeout absorbs it. Free tier: 100,000 vCore-seconds/month (~27 hours of compute)
and 32 GB. A season holds ~50,000 game lines and uses a fraction of the space;
the vCore budget is the one to watch, and a full-season replay is the heaviest
thing that touches it.

## One replica, and why it is a ceiling

`api-deploy.yml` sets `--min-replicas 0 --max-replicas 1`. The ceiling is a
**correctness** requirement, not a budget one, since the API hosts a SignalR hub.
Two things in it are per-process: the hub's connection groups (a GM on replica A
never receives a message pushed from replica B) and `PresenceRegistry` (each
replica reports the half of the league it does not hold as offline). Both fail
intermittently and only under load. Raising the ceiling needs a **backplane**
(Azure SignalR Service or Redis) *and* a shared presence store — not a bigger
number.

**The awake-time budget.** A WebSocket is a request that never ends, so the app
cannot scale to zero while anyone is connected. The free grant (180,000 vCPU-s +
360,000 GiB-s a month, at the default 0.5 vCPU / 1 GiB) buys roughly **100 hours
of an awake replica per month**, past which it is pay-as-you-go, on the order of
$30-35/month if it never sleeps. Billing is per replica-second, not per
connection: five GMs connected at once cost what one does.

What keeps it inside the grant is `frontend/src/live/LiveProvider.tsx`: connect
when a league loads and the tab is visible, drop 60 s after the tab is hidden,
stop immediately on `pagehide`. **The hidden tab is the whole lever**, and **if
the awake hours ever look wrong, read that file first.** There is deliberately no
idle timeout and no activity tracking — it bought little on top of
`visibilitychange` and could hammer `/hubs/live/negotiate` once per scroll event
whenever the API was cold. Reconnection after a drop is
`withAutomaticReconnect()`'s job.

---

## Local dev

Credentials: `backend/FantasyWarrior.{Jobs,Api}/appsettings.Local.json`, or the
`AZURE_SQL_CONNECTION` environment variable, which wins. Nothing else.

```powershell
dotnet run --project backend/FantasyWarrior.Api --no-launch-profile   # :5099
cd frontend && npm run dev                                            # :5173
dotnet test FantasyWarrior.slnx
```

Integration tests use **LocalDB** (`MSSQLLocalDB`), creating and dropping
`FantasyWarriorTests` each run — they never touch Azure. `FW_TEST_SQL_CONNECTION`
points them elsewhere; with no SQL Server at all they skip cleanly.
`dotnet-ef` is needed only to author migrations:
`dotnet tool install --global dotnet-ef --version 10.0.10`.

## Jobs

`dotnet run --project backend/FantasyWarrior.Jobs -- <job>`. The comment block at
the top of `Jobs/Program.cs` documents every option; that block is the reference,
this is the map. Almost every job takes `--dry-run` — use it.

| Need | Command |
|---|---|
| Apply the schema | `db-migrate [--list]` |
| Declare a season's dates | `season-init --season 20262027 --start 2026-10-06 --end 2027-04-15` |
| List what is declared | `season-init` |
| Generate a season's calendar | `period-init --season 20262027` |
| Generate a league's draft picks | `draft-picks-init --league <joinCode> [--year YYYY]` |
| Run the scoring (nightly entry point) | `nightly` |
| Catch up a missed cron or an imported season | `nightly --backfill-from N` |
| Re-score one week of one league | `period-rollup --league <id> --week N` |
| Move a league one phase | `season-phase --league <joinCode> --to <Phase>` |
| Clear a league's protections | `protection-reset --league <joinCode>` |
| Move a week's lock | `UPDATE Periods SET LockUtc = … WHERE Season = … AND Number = …` |
| Un-bank to recompute | `UPDATE RosterAssignments SET IsFinalized = 0` and `UPDATE Periods SET FinalizedUtc = NULL`, then `nightly --backfill-from N` |

### Moving a live database onto the rules document

A database that predates `LeagueSeasons.Rules` needs **two deploys**, in this
order. It is not optional and the schema enforces it: `DropLegacyLeagueRules`
refuses to apply while any season still holds the `'{}'` default, because after
it the columns the conversion reads are gone.

1. Deploy **`2db7f52`** (*Every consumer reads the season's rules…*), then, in one
   sitting:
   ```powershell
   dotnet run --project backend/FantasyWarrior.Jobs -- db-migrate
   dotnet run --project backend/FantasyWarrior.Jobs -- rules-backfill --dry-run
   dotnet run --project backend/FantasyWarrior.Jobs -- rules-backfill
   ```
   Between the migration and the backfill every league's rules read as "never
   written" and the app refuses to trade, score or draft — seconds, not hours, so
   run the two together.
2. Deploy **`4653b13`** (*Delete the old home…*) and `db-migrate` again. The
   guard passes, the columns and `LeagueScoringRules` go, and `vStandings` is
   rebuilt reading `cap.defaultCapHit` out of the document.

A **fresh** database skips all of this: `seed-mordus` and league creation both
write the document as they build the season, and `rules-backfill` no longer
exists after step 2 — it went with the columns it read.

Once converted, Les Mordus' three off-season numbers are entered from the rules
panel like any other rule.

**`period-init` is idempotent and runs nightly**, after `stats-sync` and before
scoring. It appends weeks the calendar is missing and refreshes `GameCount` on
weeks that are not finalized — the counts on a season built from declared dates
start at zero and catch up as the schedule imports. **Boundaries are never
moved**: points are banked against them.

Opening a new season is therefore two commands, in this order:
`season-init --season <s> --start … --end …`, then `period-init --season <s>`.
The second refuses only when neither source knows anything — no declared row and
no games — and says which command to run.

A full-season backfill is an ordinary operation on Azure SQL — no read quota to
spare. There is **no `set-league-rules`, no `sim-reset` and no `recompute` job**,
whatever other text may claim. Season replay commands live in
[testmode.md](testmode.md).

### Rebuilding the database from nothing

```powershell
dotnet run --project backend/FantasyWarrior.Jobs -- db-migrate
dotnet run --project backend/FantasyWarrior.Jobs -- player-sync --season 20252026
dotnet run --project backend/FantasyWarrior.Jobs -- stats-sync --from 2025-10-07 --to 2026-04-16  # ~10 min
# 20252026 is seeded by the migration; season-init is only needed for a new season.
dotnet run --project backend/FantasyWarrior.Jobs -- period-init --season 20252026
dotnet run --project backend/FantasyWarrior.Jobs -- player-resolve            # before seeding
dotnet run --project backend/FantasyWarrior.Jobs -- draft-sync
dotnet run --project backend/FantasyWarrior.Jobs -- career-sync
dotnet run --project backend/FantasyWarrior.Jobs -- capwages-sync --resolve-unmatched   # ~15 min
dotnet run --project backend/FantasyWarrior.Jobs -- seed-mordus
dotnet run --project backend/FantasyWarrior.Jobs -- sim-clock --set 2025-10-04 --season 20252026
```

`career-sync [--limit N] [--max-age-days N]` refreshes the stalest players
rather than syncing once, because the current season's row changes all year.
The window defaults to **30 days**, which is the exact staleness ceiling on
`Players.CareerNhlGames` — and the reason a draft should freeze that number
rather than read it live. It is **not** in the nightly chain.

⚠️ **To replay the pre-SQL oracle comparison, seed with `--no-opening-lineup`.**
The old engine never used the source PDF's Active list, it auto-filled. Both
produce legitimate but different scores, and `golden-scores-preSql.json` only
validates the engine if the *inputs* match — otherwise you get a large,
plausible, meaningless diff.

`wipe-pools` clears leagues, teams, rosters and un-banks every week while leaving
players, contracts, games and game lines alone — that half costs hours to rebuild
and is identical for everyone.

**`player-resolve` runs before `seed-mordus`, not after**, because `seed-mordus`
refuses to write a league whose roster file names a player it cannot find and
`player-sync` cannot see everyone (`--file data/unresolved-players.txt`; how it
resolves them is in [integrations.md](integrations.md)). Always `--dry-run` first
and read the list of names it could not place.

### Rehearsing the off-season — `clone-league`

**The way to do it.** `clone-league` copies the rules and the rosters into a
brand new league and leaves the original untouched, so there is nothing to
undo — throw the copy away instead.

```powershell
dotnet run --project backend/FantasyWarrior.Jobs -- clone-league `
  --from TKW6UR --name Mordus2 --drafting `
  --protection-slots 9 --steal-rounds 2 --max-losses 2 --dry-run
```

Drop `--dry-run` to write. In one pass it copies the league's rules, its teams
(the same GM accounts — a `User` is global) and its **open** roster spots, opens
a `LeagueSeason` for the season after the one being played, generates the
rookie-segment picks, auto-fills the protections and freezes the order.

- **The weeks are not copied** — no assignments, lineups, trades or history, so
  the copy's standings are empty by design. **Which is why the order is
  borrowed**: with no standings of its own, `--drafting` freezes **the source
  league's** onto it.
- **The three rule flags apply to the copy only** — never to the source, a live pool.
- **`--commissioner-only`** keeps the copy out of the other GMs' league lists.
  Without it every owner gets a membership row and sees the new league appear.
- **The nightly still scores a copy.** `period-rollup` iterates every league and
  knows nothing about phases, so a copy sitting in `Drafting` gets this week
  auto-filled and scored like any other. Harmless, but its standings will not
  stay at zero — it accrues from the week it was created in, since weeks already
  banked are not re-scored (`Periods.FinalizedUtc` is global).

**Throwing a copy away.** A copy owns no banked history, so this is a plain
delete rather than an unwind — never run it against a league you did not clone.
In one transaction, with `@l` = its `LeagueId`, delete in this order (foreign
keys make it load-bearing): `DraftSelections` (by its `LeagueSeasonId`s),
`RosterAssignments` (by its `RosterSpotId`s), `TeamPeriodLineups` (by its
`TeamId`s), `RosterSpots`, `TradeVotes` and `TradeAssets` (by its `TradeId`s),
`Trades`, `Messages`, `DraftPicks`, `LeagueSeasons`, `LeagueMembers`,
`LeagueSeasons`, `Teams`, `Leagues` — the last eleven all `WHERE LeagueId = @l`.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| *"Database is not currently available. Please retry the connection later."* | Usually *Deny Public Network Access*, not a paused database — the message is misleading | Connect to `master` instead and the real error appears; re-check Networking |
| `Client with IP address 'x.x.x.x' is not allowed to access the server` | The Container App's outbound IP is not in the firewall — "Allow Azure services" does not cover it | `api-deploy.yml` writes those rules itself; Azure SQL takes up to 5 min to apply one, so retry before assuming failure |
| `The configured execution strategy does not support user-initiated transactions` | `EnableRetryOnFailure` is on | Go through `db.Database.CreateExecutionStrategy()`. Not a formality: a retry must replay the whole transaction, not the surviving half |
| `Replacement index 1 out of range for positional args tuple` during a deploy | A bug in the `containerapp` extension's own error formatting, hit while it tries and fails to auto-register a provider — it hides the real error | The two `az provider register` commands above, once, as a subscription owner |
| Every screen but News says "failed to fetch" | A pending migration. `GET /api/leagues/{id}` throws `Invalid column name '…'`, and every screen loads league detail first — News is the one that never asks. It reads as `Failed to fetch` not `HTTP 500` because an unhandled exception's response carries no CORS headers | `db-migrate` |
| The nightly chain has been dead for weeks and nobody noticed | `daily-jobs.yml` announces nothing when it fails, and every step is downstream of *Apply migrations* | Read the step log. **Exit 134 is a .NET job aborting on an unhandled exception** (SIGABRT), not a step-specific code. A green *Apply migrations* usually means nothing was pending, not that CI can migrate — check the migration's timestamp against the run |
| `news-sync` reports FantasySP 403 Forbidden | The site blocks the scraper (client fingerprinting, deliberately not chased) | Nothing. Rotowire's two sources still work, and the job logs the status rather than reporting a silent zero |
| A local build fails on a locked output file | A stale `dotnet run` still holds it | Kill the `FantasyWarrior.Api` or `FantasyWarrior.Jobs` process and rebuild |

**The API answers the TLS handshake and then hangs forever** — ingress up, no
healthy replica behind it. Two causes, and the second is the one to know.

*The container crash-loops.* The connection string resolves lazily, so `/health`
answers whatever the database is doing and `/health/db` reports the real reason
separately. Resolving it at service-registration time throws before the web host
exists, which is what produces this symptom.

*`ImagePullUnauthorized`* — **the nastier one, because the deploy that causes it
goes green.** Never store registry credentials on the app. `GITHUB_TOKEN` dies
when the run ends, and a Container App that has stored credentials *always* uses
them and never falls back to an anonymous pull, so the pull returns 401 even
though the ghcr package is public. The running replica still holds the image, so
the deploy passes; the app only dies at the next wake-up from `min-replicas 0`.
`api-deploy.yml` removes the credential on every deploy. To repair an app still
carrying one — and if the package ever goes private, use a PAT with
`read:packages`, never `GITHUB_TOKEN`:

```bash
az containerapp registry remove -n fantasy-warrior-api -g fw --server ghcr.io
az containerapp revision restart -n fantasy-warrior-api -g fw --revision <rev>
```

**Reading Container Apps logs.** `--type console` shows what the app wrote;
**`--type system` shows what Container Apps did to it.** A failed pull writes
nothing to console, so an empty console log is a signal, not a dead end. And
`az containerapp logs show` reporting *"Successfully connected to container"*
does **not** mean the image is good: a replica stuck in `ImagePullBackOff` still
has a replica object with no container inside it.

---

## Appendix — emergency: unwinding a live league out of `Drafting`

**Use `clone-league` instead.** This is only for when the *real* league has
genuinely had to move, and it is irreversible work on live data.

Driving a live league in: `season-phase --to Complete`, `--to Preparing`,
`--to Protecting`, then `draft-picks-init --league <code>`, then the app's Draft
tab as commissioner — "Auto-protect each roster", then "Open the draft". Four
things that will bite otherwise:

- ⚠️ **`season-phase --to Complete` writes a champion off today's standings.**
  Run mid-season — which is exactly when a rehearsal happens — it stamps
  whoever is leading into `LeagueSeasons.ChampionTeamId`, and the palmarès then
  publishes that false champion to the whole league. Every `season-phase` step
  takes `--dry-run`; use it.
- ⚠️ **Never `season-phase --to Drafting`.** Only `POST /draft/open` freezes
  `DraftPick.PickInRound` from the reversed standings. The job would set the
  phase alone and the rookie segment would then read nulls for every pick. The
  commissioner's route into `Drafting` is the button in the app, which is why the
  Draft tab already appears during `Protecting` for the commissioner alone.
- **`draft-picks-init` is not optional.** `draft/open` refuses unless the pick
  count is exactly `teams × DraftRounds` (`DraftEndpoints.cs:367`) and says so.
- **Call the autofill with `?preview=true` first** on a live league: it returns
  the same counts and writes nothing (`DraftEndpoints.cs:170-222`).

**What the league sees while this runs.** Trades freeze, and a Draft tab with a
LIVE pill appears **for everyone** the moment the room opens — this is not a
quiet rehearsal. The palmarès reports a champion for a season that never
finished. What does *not* move is `Leagues.Season`: it only advances on the way
into `InSeason`, which a rehearsal never reaches, so standings, lineups and
scoring carry on and the nightly keeps banking weeks underneath.

**There is no forward path back.** `Drafting → PreSeason → InSeason` points
`Leagues.Season` at the prepared season, which has no `Games` and no `Periods`
until the NHL publishes its schedule, and the standings would empty. So the undo
is hand-written SQL, in one transaction, with `@l` = the `LeagueId`, `@s3`/`@s4`
= the played and prepared `LeagueSeasonId`s, `@d` = the date the rehearsal
started on:

```sql
DELETE FROM DraftSelections WHERE LeagueSeasonId = @s4;               -- the turn log
DELETE FROM RosterSpots                                               -- spots a pick opened
 WHERE LeagueId = @l AND StartReason = 1 AND StartDate > @d;
UPDATE RosterSpots SET EndDate = NULL, EndReason = NULL               -- spots a steal closed
 WHERE LeagueId = @l AND EndReason = 2;
UPDATE DraftPicks SET UsedUtc = NULL, PickInRound = NULL              -- entitlements + frozen order
 WHERE LeagueId = @l AND Year = @draftYear;
DELETE FROM LeagueSeasons WHERE LeagueSeasonId = @s4;                 -- the prepared season
UPDATE LeagueSeasons SET Phase = 4, ChampionTeamId = NULL, CompletedUtc = NULL
 WHERE LeagueSeasonId = @s3;                                          -- 4 = InSeason
```

Then `protection-reset --league <code>`, and confirm `Leagues.Season` never moved
— it only changes on the way into `InSeason`, which a rehearsal never reaches.

⚠️ **The `RosterSpots` delete is the sharp edge.** `StartReason = Draft` cannot
tell today's pick from a spot `seed-mordus` opened in October — the seed used
that same reason for every original spot. **The `StartDate > @d` bound is the
only thing that makes the delete safe.** Run it as a `SELECT COUNT(*)` first and
check the number against how many picks were actually made.
