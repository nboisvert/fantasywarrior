using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Seasons;
using FantasyWarrior.Jobs.CapWages;
using FantasyWarrior.Jobs.News;
using FantasyWarrior.Jobs.Nhl;
using FantasyWarrior.Jobs.Ops;
using FantasyWarrior.Jobs.Sql;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

// Usage: dotnet run -- <job> [options]
//
// The connection string is resolved from AZURE_SQL_CONNECTION, then
// appsettings.Local.json, then appsettings.json. Nothing else is needed.
//
// --- database ---
//   db-migrate [--list]
//     Brings the schema up to the latest migration. Deliberately a command
//     rather than a startup hook: Cloud Run can start several instances at
//     once and they must not race into the same schema change.
//
// --- NHL and contract ingestion ---
//   player-sync [--season 20252026] [--dry-run]
//     Every team's roster and prospects. Owns only the fields it syncs; draft
//     details, the CapWages slug and contracts are left alone.
//   stats-sync [--date YYYY-MM-DD | --from A --to B]   (default: ET yesterday)
//     Finished games and their boxscore lines. A full season is ~1,300 games
//     and ~50,000 lines and takes about ten minutes.
//   draft-sync [--limit N]
//     Entry-draft details, one HTTP call per player never checked before.
//   career-sync [--limit N] [--max-age-days N]  (default 30)
//     Season-by-season career stats (GP/G/A/PTS/PIM, the goalie equivalent) for
//     the Player Card's Career tab. Refreshes the stalest players first rather
//     than fetching once forever, since the current season's row keeps
//     changing. Not on the nightly cron yet -- run manually until validated.
//   capwages-sync [--season] [--dry-run] [--resolve-unmatched]
//     Real contracts from capwages.com, read out of the JSON each page embeds
//     for its own React tree rather than the rendered tables, so a layout
//     change cannot break it. 32 requests, 2s apart, honest User-Agent.
//     Personal/non-commercial use only.
//   news-sync [--rotowire-url <u>] [--rotowire-injuries-url <u>] [--fantasysp-url <u>]
//
// --- calendar and scoring ---
//   season-init [--season 20262027 --start YYYY-MM-DD --end YYYY-MM-DD]
//               [--playoff-start] [--playoff-end] [--dry-run]
//     Declares an NHL season's dates, from the schedule the NHL publishes. With
//     no --season it lists what is declared. This is what lets period-init build
//     next season's calendar before a single game has been synced, and what
//     every job's --season default resolves against.
//   period-init [--season 20252026] [--dry-run]
//     The weekly calendar, over the declared dates and the games we hold
//     reconciled (SeasonBounds). Boundaries are append-only — moving one would
//     restate points teams already own — but GameCount is refreshed on weeks
//     that are not finalized, so a calendar built before the schedule arrived
//     stops reading as a season of break weeks.
//   nightly [--dry-run] [--backfill-from N]
//     THE nightly entry point, and the only place the correct order lives:
//     score the current week -> bank finished weeks -> execute accepted
//     trades effective next week.
//   period-rollup [--league <id>] [--week N] [--dry-run]
//     Scores one week into RosterAssignment rows. Everything above that grain
//     is a view, so this writes nothing else.
//   standings-snapshot [--league <id>] [--dry-run]
//     Writes one TeamStandingsSnapshot per team for last night — rank and
//     what the active roster scored specifically that day. Also runs as
//     nightly's own step [4/4]; standalone here for testing without touching
//     scoring, banking or trade execution.
//   protection-reset --league <joinCode> [--dry-run]
//     Clears every off-season protection in a league. A protection is worth one
//     summer and expires when the season it guarded begins, which is why the
//     status is a column on the spot rather than a row per draft.
//   season-phase --league <joinCode> --to <Phase> [--dry-run]
//     Moves a league's active LeagueSeason one step: Preparing -> Protecting ->
//     Drafting -> PreSeason -> InSeason -> Complete. Run by a commissioner's
//     decision, never a clock. --to Preparing with no open season opens the next
//     one. --to InSeason flips League.Season and clears protections; --to
//     Complete writes the champion off vStandings. See offseason.md.
//   draft-picks-init --league <joinCode> [--year YYYY] [--dry-run]
//     One pick per team per round for one season, defaulting to the season
//     after the current one. Picks exist one year ahead and only one, which is
//     what makes "tradable a year in advance" true without a rule saying so.
//
//   player-resolve [--file data/unresolved-players.txt] [--dry-run]
//     Adds players player-sync cannot see, from a list of names. An unsigned
//     free agent is on no roster and a fresh draftee on no prospect list, so
//     neither endpoint the roster sync reads will ever return them. Resolves
//     each name against the NHL search endpoint and writes only what is
//     unambiguous — the rest is reported, never guessed.
//
// --- league setup ---
//   seed-mordus [--file data/mordus-rosters.json] [--season] [--commissioner]
//               [--cap] [--floor] [--join-code] [--dry-run] [--no-opening-lineup]
//     Creates "Les Mordus" from the rosters imported out of Nick's PoolExpert
//     PDF. --floor sets the cap floor (Cap.Min, null by default). --join-code
//     keeps a known code instead of drawing a random one -- for rebuilding the
//     same league fresh (delete-league first) without stranding GMs who have
//     the old code bookmarked. --no-opening-lineup leaves week 1 to be
//     auto-filled, which is what the Firestore build did and the only setting
//     under which a replay can be compared against golden-scores-preSql.json.
//   clone-league --from <joinCode> --name <name> [--drafting] [--commissioner-only]
//               [--protection-slots N] [--steal-rounds N] [--max-losses N] [--dry-run]
//     Copies a league's rules and rosters into a new one -- and nothing else.
//     No weeks, no lineups, no trades, no history: the copy has never played a
//     game. --drafting takes it straight to the draft room (protections
//     auto-filled, order frozen off the SOURCE league's standings, since the
//     copy has none of its own). --commissioner-only keeps it out of the other
//     GMs' league lists, which is the difference between a sandbox and an
//     announcement. The three rule flags override the copy's off-season rules
//     only -- never the source's, which is a live pool.
//   wipe-pools [--dry-run]
//     Deletes pool data and un-banks every week. NHL reference data is
//     untouched -- that is the expensive half to rebuild.
//   delete-league --name <exact league name> [--delete-users [--keep-user <u>]] [--dry-run]
//     Permanently deletes one league by name -- the same load-bearing order as
//     seed-mordus2's own wipe (see deployment.md "Throwing a copy away"). Only
//     for a league with no banked history (a clone/rehearsal copy, or a real
//     league whose history is being deliberately discarded); nothing here
//     un-banks a week. --delete-users also deletes every team owner's User
//     row (and their CockcoinAwards) -- except --keep-user, and except anyone
//     who still owns a team in a DIFFERENT league, checked again right before
//     the delete so this can never strand an unrelated league.
//   reset-mordus-rosters [--file data/mordus-2026-27.json] [--dry-run]
//     Wipes Les Mordus's roster spots (and, by cascade, the RosterAssignments
//     scored against them) and rebuilds them from a fresh PoolExpert export --
//     the league, its Users, Teams, Trades and Messages are left alone. Refuses
//     unless League.Season already equals the file's season (season-phase
//     --to InSeason) and that season's period calendar already exists
//     (season-init, period-init).
//   set-optimal-lineup [--league TKW6UR] [--week 1] [--stats-season] [--dry-run]
//     Sets one week's active lineup for every team to the best F/D/G split
//     available under the league's own scoring scale, ranked on a prior
//     season's totals (--stats-season, default: the season before League.
//     Season). For opening a season nobody has set a real lineup for yet.
//     Updates RosterAssignments.IsActive for spots that already have a row for
//     the period (seed-mordus/reset-mordus-rosters both create one per spot);
//     creates one for any that don't. Never touches the Équipe (`T`) spot.
//   dump-mordus-rosters [--file data/mordus-2026-27-seed.json]
//     Writes Les Mordus's current roster spots out in seed-mordus's own file
//     shape (resolved playerIds, not names) -- for rebuilding via seed-mordus
//     without re-resolving names reset-mordus-rosters already resolved once.
//   fix-season-number --league <joinCode> --number N
//     Corrects LeagueSeasons.Number on a league's active season -- the pool's
//     own lifetime season count, not derivable from anything else, and easy
//     to get wrong on a rebuild (seed-mordus defaults to 3 unless told).
//   injury-report [--league TKW6UR]
//     Every currently-injured/suspended player on the league's rosters
//     (PlayerInjuries.ResolvedUtc == null), grouped by team. Read-only.
//   list-leagues-and-users
//     Prints every league, user and team in the database. Run before a
//     delete-league --delete-users to confirm its scope -- a commissioner's
//     account is often shared with an unrelated personal league.
//
// --- season simulation (test mode) ---
//   sim-clock [--set YYYY-MM-DD] [--season] [--off]
//   sim-advance --to YYYY-MM-DD [--dry-run]
//     Replays day by day, running each evening as the nightly pipeline would.
//     Only the NHL fetch is skipped. Stops at every week end it crosses, so
//     trades execute at the boundary they were accepted before. Forward only.

var job = args.FirstOrDefault();
if (job is null)
{
    Console.Error.WriteLine("Usage: FantasyWarrior.Jobs <job> [options]");
    return 1;
}

static string? GetOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

// Which season a job means when it is not told. The declared calendar answers
// it when we have one; Season.CurrentOn — a hardcoded September cutover — stays
// as the documented fallback for an empty Seasons table rather than remaining
// the default answer it used to be.
static async Task<string> CurrentSeasonAsync(FantasyWarriorDbContext db) =>
    await SeasonLookup.CurrentOrGuessAsync(db, DateOnly.FromDateTime(DateTime.UtcNow));

// An identifiable User-Agent, per the vendor guide — never a browser's, since
// the point is that these sites can see who is calling.
//
// FantasySP began answering this client with 403 on 2026-08-04, from an IP and
// a User-Agent that curl got 200 on seconds later; adding Accept headers and
// HTTP/2 changed nothing, so it is the client fingerprint, not the request.
// Deliberately not chased further — dressing this up as a browser would be
// circumventing an access control the site chose to put up. The scraper
// already treats a failed fetch as "unknown" rather than "nobody is hurt", so
// the cost of being turned away is that FantasySP's injuries stop updating,
// not that they vanish.
static HttpClient NewHttp()
{
    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("FantasyWarrior/0.1");
    return http;
}

var dryRun = args.Contains("--dry-run");

switch (job)
{
    case "db-migrate":
        return await DbMigrateJob.RunAsync(listOnly: args.Contains("--list"));

    case "player-sync":
    {
        using var http = NewHttp();
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new PlayerSyncJob(new NhlApiClient(http), db)
            .RunAsync(GetOption(args, "--season") ?? await CurrentSeasonAsync(db), dryRun);
    }

    case "stats-sync":
    {
        using var http = NewHttp();
        await using var db = DataServiceCollectionExtensions.CreateContext();
        var single = GetOption(args, "--date");
        var yesterday = PoolClock.LastStatDate(DateTimeOffset.UtcNow);
        var from = DateOnly.Parse(GetOption(args, "--from") ?? single ?? yesterday.ToString("yyyy-MM-dd"));
        var to = DateOnly.Parse(GetOption(args, "--to") ?? single ?? yesterday.ToString("yyyy-MM-dd"));
        if (to < from) { Console.Error.WriteLine("--to must be >= --from"); return 1; }
        await new StatsSyncJob(new NhlApiClient(http), db).RunAsync(from, to);
        return 0;
    }

    case "draft-sync":
    {
        using var http = NewHttp();
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new DraftSyncJob(new NhlApiClient(http), db)
            .RunAsync(int.TryParse(GetOption(args, "--limit"), out var limit) ? limit : null);
    }

    case "player-resolve":
    {
        using var http = NewHttp();
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new PlayerResolveJob(new NhlApiClient(http), db)
            .RunAsync(GetOption(args, "--file") ?? "data/unresolved-players.txt", dryRun);
    }

    case "add-player":
    {
        // A single explicit insert for the rare case where player-resolve's
        // surname search comes back genuinely ambiguous (several real NHLers
        // share a full name) and a human has confirmed which NHL id is meant.
        await using var db = DataServiceCollectionExtensions.CreateContext();
        var id = long.Parse(GetOption(args, "--id") ?? throw new ArgumentException("--id required"));
        if (await db.Players.AnyAsync(p => p.PlayerId == id))
        {
            Console.WriteLine($"Player {id} already exists.");
            return 0;
        }
        var player = new FantasyWarrior.Data.Entities.Player
        {
            PlayerId = id,
            FirstName = GetOption(args, "--first") ?? throw new ArgumentException("--first required"),
            LastName = GetOption(args, "--last") ?? throw new ArgumentException("--last required"),
            Position = GetOption(args, "--pos") ?? "C",
            TeamAbbrev = GetOption(args, "--team"),
            Status = GetOption(args, "--status") ?? FantasyWarrior.Data.Entities.PlayerStatus.Nhl,
            BirthDate = GetOption(args, "--birth") is { } b ? DateOnly.Parse(b) : null,
            LastSyncedUtc = DateTime.UtcNow,
        };
        Console.WriteLine($"=== add-player{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"  {player.PlayerId}  {player.FirstName} {player.LastName} "
            + $"({player.Position}, {player.TeamAbbrev}, {player.Status})");
        if (dryRun) return 0;
        db.Players.Add(player);
        await db.SaveChangesAsync();
        Console.WriteLine("Added.");
        return 0;
    }

    case "career-sync":
    {
        using var http = NewHttp();
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new CareerStatsSyncJob(new NhlApiClient(http), db).RunAsync(
            int.TryParse(GetOption(args, "--limit"), out var careerLimit) ? careerLimit : null,
            int.TryParse(GetOption(args, "--max-age-days"), out var maxAgeDays) ? maxAgeDays : 30);
    }

    case "capwages-sync":
    {
        using var http = NewHttp();
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new CapWagesSyncJob(db, new CapWagesClient(http)).RunAsync(
            onlySeason: GetOption(args, "--season"),
            dryRun: dryRun,
            resolveUnmatched: args.Contains("--resolve-unmatched"));
    }

    case "news-sync":
    {
        using var http = NewHttp();
        await using var db = DataServiceCollectionExtensions.CreateContext();
        var rss = new RssNewsClient(http);
        var rotowire = new RotowireInjuryScraper(http);
        var fantasySp = new FantasySpScraper(http);
        var rotoRss = GetOption(args, "--rotowire-url") ?? "https://www.rotowire.com/rss/news.php?sport=NHL";
        var rotoInj = GetOption(args, "--rotowire-injuries-url") ?? "https://www.rotowire.com/hockey/news.php?view=injuries";
        var fspUrl = GetOption(args, "--fantasysp-url") ?? "https://www.fantasysp.com/injuries/nhl/";
        return await new NewsSyncJob(db).RunAsync(
        [
            new NewsSource("rotowire_rss", ct => rss.GetItemsAsync(rotoRss, ct), HasReliablePublishedDate: true),
            // Rotowire's injuries page does print a per-item date
            // (news-update__timestamp) — the first version of the scraper
            // simply did not read it.
            new NewsSource("rotowire_html", ct => rotowire.GetInjuryItemsAsync(rotoInj, ct), HasReliablePublishedDate: true, IsInjuryList: true),
            new NewsSource("fantasysp", ct => fantasySp.GetInjuryItemsAsync(fspUrl, ct), HasReliablePublishedDate: false, IsInjuryList: true),
        ]);
    }

    case "season-init":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new SeasonInitJob(db).RunAsync(
            GetOption(args, "--season"),
            GetOption(args, "--start"),
            GetOption(args, "--end"),
            GetOption(args, "--playoff-start"),
            GetOption(args, "--playoff-end"),
            dryRun);
    }

    case "period-init":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new PeriodInitJob(db).RunAsync(GetOption(args, "--season") ?? await CurrentSeasonAsync(db), dryRun);
    }

    case "draft-picks-init":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        // Year defaults to the season after the current one: picks are always
        // generated one year ahead, never for the season being played.
        var defaultYear = Season.StartYear(await CurrentSeasonAsync(db)) + 1;
        return await new DraftPicksInitJob(db).RunAsync(
            GetOption(args, "--league"),
            int.TryParse(GetOption(args, "--year"), out var draftYear) ? draftYear : defaultYear,
            dryRun);
    }

    case "protection-reset":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new ProtectionResetJob(db).RunAsync(GetOption(args, "--league"), dryRun);
    }

    case "season-phase":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new SeasonPhaseJob(db).RunAsync(
            GetOption(args, "--league"), GetOption(args, "--to"), dryRun);
    }

    case "period-rollup":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new PeriodRollupJob(db).RunAsync(
            onlyLeagueId: int.TryParse(GetOption(args, "--league"), out var league) ? league : null,
            dryRun: dryRun,
            nowOverride: null,
            onlyPeriodNumber: int.TryParse(GetOption(args, "--week"), out var week) ? week : null);
    }

    case "standings-snapshot":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new StandingsSnapshotJob(db).RunAsync(
            onlyLeagueId: int.TryParse(GetOption(args, "--league"), out var snapLeague) ? snapLeague : null,
            dryRun: dryRun);
    }

    case "nightly":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new NightlyJob(db).RunAsync(
            dryRun, int.TryParse(GetOption(args, "--backfill-from"), out var from) ? from : null);
    }

    case "seed-mordus":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new SeedMordusJob(db).RunAsync(
            file: GetOption(args, "--file") ?? "data/mordus-rosters.json",
            season: GetOption(args, "--season") ?? await CurrentSeasonAsync(db),
            commissioner: GetOption(args, "--commissioner") ?? "nick",
            // The league's real cap (Nick, 2026-08-05). It was seeded at
            // $115M — the NHL's own number — which is not the rule the Mordus
            // play by, and which put two teams over budget on paper.
            capAmount: long.TryParse(GetOption(args, "--cap"), out var cap) ? cap : 134_000_000,
            dryRun: dryRun,
            openingLineup: !args.Contains("--no-opening-lineup"),
            capFloor: long.TryParse(GetOption(args, "--floor"), out var floor) ? floor : null,
            joinCode: GetOption(args, "--join-code"),
            seasonNumber: int.TryParse(GetOption(args, "--season-number"), out var num) ? num : 3);
    }

    case "seed-mordus2":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new SeedMordus2Job(db).RunAsync(
            file: GetOption(args, "--file") ?? "data/mordus2.json",
            dryRun: dryRun);
    }

    case "clone-league":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        static int? Number(string[] args, string name) =>
            int.TryParse(GetOption(args, name), out var n) ? n : null;
        return await new CloneLeagueJob(db).RunAsync(
            sourceCode: GetOption(args, "--from"),
            name: GetOption(args, "--name"),
            drafting: args.Contains("--drafting"),
            everyOwnerJoins: !args.Contains("--commissioner-only"),
            protectionSlots: Number(args, "--protection-slots"),
            stealRounds: Number(args, "--steal-rounds"),
            maxLosses: Number(args, "--max-losses"),
            dryRun: dryRun);
    }

    case "wipe-pools":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new WipePoolsJob(db).RunAsync(dryRun);
    }

    case "delete-league":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        var name = GetOption(args, "--name");
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("delete-league needs --name <exact league name>.");
            return 1;
        }
        var toDelete = await db.Leagues.FirstOrDefaultAsync(l => l.Name == name);
        if (toDelete is null)
        {
            Console.WriteLine($"No league named \"{name}\". Nothing to do.");
            return 0;
        }
        var id = toDelete.LeagueId;
        var deleteUsers = args.Contains("--delete-users");
        var keep = (GetOption(args, "--keep-user") ?? "").Trim().ToLowerInvariant();

        Console.WriteLine($"=== delete-league{(dryRun ? "  [DRY RUN]" : "")}  \"{name}\" (join code {toDelete.JoinCode}) ===");
        var counts = new (string Name, int Count)[]
        {
            ("RosterSpots", await db.RosterSpots.CountAsync(s => s.LeagueId == id)),
            ("Teams", await db.Teams.CountAsync(t => t.LeagueId == id)),
            ("Trades", await db.Trades.CountAsync(t => t.LeagueId == id)),
            ("Messages", await db.Messages.CountAsync(m => m.LeagueId == id)),
            ("DraftPicks", await db.DraftPicks.CountAsync(p => p.LeagueId == id)),
        };
        foreach (var (n, c) in counts) Console.WriteLine($"  {n,-16} {c,6}");

        // Owners this league's teams belong to -- candidates for --delete-users.
        // A candidate is only actually deleted if this is the ONLY league they
        // belong to (checked again right before the delete, inside the same
        // transaction) -- a user shared with another league is never touched,
        // --keep-user or not, so this can never strand an unrelated league.
        var ownerIds = await db.Teams.Where(t => t.LeagueId == id).Select(t => t.OwnerUserId).Distinct().ToListAsync();
        if (deleteUsers)
        {
            Console.WriteLine($"  --delete-users: {ownerIds.Count} owner(s), keeping \"{keep}\" if present.");
        }
        if (dryRun) { Console.WriteLine("\n[DRY RUN] Nothing deleted."); return 0; }

        // Same load-bearing order as SeedMordus2Job's own wipe (see deployment.md
        // "Throwing a copy away") -- never run this against a league with banked
        // history, since nothing here un-banks a week.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.RosterAssignments.Where(ra => ra.RosterSpot!.LeagueId == id).ExecuteDeleteAsync();
            await db.RosterSpots.Where(s => s.LeagueId == id).ExecuteDeleteAsync();
            await db.TradeAssets.Where(a => a.Trade!.LeagueId == id).ExecuteDeleteAsync();
            await db.TradeVotes.Where(v => v.Trade!.LeagueId == id).ExecuteDeleteAsync();
            await db.Messages.Where(m => m.LeagueId == id).ExecuteDeleteAsync();
            await db.TeamPeriodLineups.Where(l => l.Team!.LeagueId == id).ExecuteDeleteAsync();
            await db.Trades.Where(t => t.LeagueId == id).ExecuteDeleteAsync();
            await db.DraftSelections.Where(s => s.LeagueSeason!.LeagueId == id).ExecuteDeleteAsync();
            await db.DraftPicks.Where(p => p.LeagueId == id).ExecuteDeleteAsync();
            await db.LeagueSeasons.Where(s => s.LeagueId == id).ExecuteDeleteAsync();
            await db.LeagueMembers.Where(m => m.LeagueId == id).ExecuteDeleteAsync();
            await db.Teams.Where(t => t.LeagueId == id).ExecuteDeleteAsync();
            await db.Leagues.Where(l => l.LeagueId == id).ExecuteDeleteAsync();

            if (deleteUsers)
            {
                foreach (var ownerId in ownerIds)
                {
                    var user = await db.Users.FindAsync(ownerId);
                    if (user is null || user.Username == keep) continue;
                    if (await db.Teams.AnyAsync(t => t.OwnerUserId == ownerId))
                    {
                        Console.WriteLine($"  keeping {user.Username} -- still owns a team in another league.");
                        continue;
                    }
                    await db.CockcoinAwards.Where(a => a.UserId == ownerId).ExecuteDeleteAsync();
                    db.Users.Remove(user);
                    Console.WriteLine($"  deleted user {user.Username}.");
                }
                await db.SaveChangesAsync();
            }

            await tx.CommitAsync();
        });
        Console.WriteLine($"\nDeleted \"{name}\".");
        return 0;
    }

    case "fix-season-number":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        var code = GetOption(args, "--league") ?? throw new ArgumentException("--league required");
        var n = int.Parse(GetOption(args, "--number") ?? throw new ArgumentException("--number required"));
        var league = await db.Leagues.FirstAsync(l => l.JoinCode == code);
        var active = await db.LeagueSeasons
            .Where(s => s.LeagueId == league.LeagueId && s.Phase != LeagueSeasonPhase.Complete)
            .FirstAsync();
        Console.WriteLine($"{league.Name}: season number {active.Number} -> {n}");
        active.Number = n;
        await db.SaveChangesAsync();
        return 0;
    }

    case "injury-report":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        var code = GetOption(args, "--league") ?? "TKW6UR";
        var league = await db.Leagues.FirstAsync(l => l.JoinCode == code);
        var rows = await db.RosterSpots
            .Where(s => s.LeagueId == league.LeagueId && s.EndDate == null && s.PlayerId != null)
            .Join(db.Teams, s => s.TeamId, t => t.TeamId, (s, t) => new { s.PlayerId, t.Name, Username = t.Owner!.Username })
            .Join(db.Players, x => x.PlayerId, p => p.PlayerId, (x, p) => new { x.Name, x.Username, p!.PlayerId, p.FirstName, p.LastName })
            .Join(db.PlayerInjuries.Where(i => i.ResolvedUtc == null), x => x.PlayerId, i => i.PlayerId,
                (x, i) => new { x.Name, x.Username, x.FirstName, x.LastName, i.Status, i.InjuryType, i.ReportedUtc })
            .OrderBy(x => x.Name).ThenBy(x => x.LastName)
            .ToListAsync();
        Console.WriteLine($"=== injury-report  {league.Name} — {rows.Count} current ===");
        foreach (var g in rows.GroupBy(r => (r.Username, r.Name)))
        {
            Console.WriteLine($"\n  {g.Key.Name} ({g.Key.Username})");
            foreach (var r in g)
                Console.WriteLine($"    {$"{r.FirstName} {r.LastName}",-24} {r.Status,-10} {r.InjuryType ?? "",-14} since {r.ReportedUtc:yyyy-MM-dd}");
        }
        return 0;
    }

    case "list-leagues-and-users":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        Console.WriteLine("=== Leagues ===");
        foreach (var l in await db.Leagues.Select(l => new { l.LeagueId, l.Name, l.Season, l.JoinCode, l.CommissionerUserId }).ToListAsync())
            Console.WriteLine($"  {l.LeagueId,3}  {l.Name,-20} season={l.Season} join={l.JoinCode} commissioner={l.CommissionerUserId}");
        Console.WriteLine("=== Users ===");
        foreach (var u in await db.Users.Select(u => new { u.UserId, u.Username, u.DisplayName }).ToListAsync())
            Console.WriteLine($"  {u.UserId,3}  {u.Username,-14} {u.DisplayName}");
        Console.WriteLine("=== Teams ===");
        foreach (var t in await db.Teams.Select(t => new { t.TeamId, t.LeagueId, t.Name, t.OwnerUserId }).ToListAsync())
            Console.WriteLine($"  {t.TeamId,3}  league={t.LeagueId,3} owner={t.OwnerUserId,3} {t.Name}");
        return 0;
    }

    case "backdate-roster-spots":
    {
        // Every "effective now" screen (Team, cap totals, vStandings' Today
        // CTE, the free-agent "engaged" flag) gates on RosterSpot.StartDate <=
        // today -- exactly right for a spot opened by a real trade ahead of
        // its first scored week, wrong for a whole roster seeded before its
        // season's week 1 has even started: the ownership is real *now*, only
        // the scoring window (already dated to week 1 via RosterAssignments,
        // untouched here) is in the future. Safe only preseason -- see
        // SeedMordusJob's own comment on why spots are dated to week 1, not
        // "now", for a MID-season import (missing assignments for weeks
        // already passed); that bug does not apply before week 1 has started.
        await using var db = DataServiceCollectionExtensions.CreateContext();
        var code = GetOption(args, "--league") ?? "TKW6UR";
        var league = await db.Leagues.FirstAsync(l => l.JoinCode == code);
        var today = PoolClock.TodayEt(DateTimeOffset.UtcNow);
        var spots = await db.RosterSpots
            .Where(s => s.LeagueId == league.LeagueId && s.EndDate == null && s.StartDate > today)
            .ToListAsync();
        Console.WriteLine($"=== backdate-roster-spots{(dryRun ? "  [DRY RUN]" : "")}  {league.Name} ===");
        Console.WriteLine($"{spots.Count} spot(s) dated after {today:yyyy-MM-dd} -> would move to {today:yyyy-MM-dd}.");
        if (dryRun) return 0;
        foreach (var s in spots) s.StartDate = today;
        await db.SaveChangesAsync();
        Console.WriteLine("Done.");
        return 0;
    }

    case "set-optimal-lineup":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        var leagueCode = GetOption(args, "--league") ?? "TKW6UR";
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.JoinCode == leagueCode);
        return await new SetOptimalLineupJob(db).RunAsync(
            leagueCode: leagueCode,
            periodNumber: int.TryParse(GetOption(args, "--week"), out var wk) ? wk : 1,
            statsSeason: GetOption(args, "--stats-season")
                ?? Season.Previous(league?.Season ?? await CurrentSeasonAsync(db)),
            dryRun: dryRun);
    }

    case "dump-mordus-rosters":
    {
        // One-off: captures the roster spots reset-mordus-rosters already
        // built and verified (every name resolved, zero guesses) into
        // seed-mordus's own file shape, so a full rebuild (wipe-pools then
        // seed-mordus) does not have to re-resolve 414 names from the PDF.
        await using var db = DataServiceCollectionExtensions.CreateContext();
        var outFile = GetOption(args, "--file") ?? "data/mordus-2026-27-seed.json";
        var league = await db.Leagues.FirstAsync(l => l.Name == "Les Mordus");
        var firstPeriod = await db.Periods.Where(p => p.Season == league.Season)
            .OrderBy(p => p.Number).FirstAsync();
        var teams = await db.Teams.Where(t => t.LeagueId == league.LeagueId)
            .Include(t => t.Owner)
            .Select(t => new { t.TeamId, t.Name, t.FranchiseAbbrev, Username = t.Owner!.Username, Gm = t.Owner!.DisplayName })
            .ToListAsync();
        var spots = await db.RosterSpots.Where(s => s.LeagueId == league.LeagueId && s.PlayerId != null)
            .Select(s => new { s.TeamId, s.RosterSpotId, s.PlayerId })
            .ToListAsync();
        var activeIds = (await db.RosterAssignments
            .Where(ra => ra.PeriodId == firstPeriod.PeriodId && ra.IsActive
                && ra.RosterSpot!.LeagueId == league.LeagueId)
            .Select(ra => ra.RosterSpotId).ToListAsync()).ToHashSet();
        var players = await db.Players.ToDictionaryAsync(p => p.PlayerId, p => p);

        var teamsJson = teams.Select(t =>
        {
            var mine = spots.Where(s => s.TeamId == t.TeamId).ToList();
            object ToEntry(long playerId) { var p = players[playerId]; return new { playerId, name = $"{p.FirstName} {p.LastName}", pos = p.Position, team = p.TeamAbbrev ?? "" }; }
            return new
            {
                gm = t.Gm, username = t.Username, franchise = t.Name, franchiseAbbrev = t.FranchiseAbbrev,
                active = mine.Where(s => activeIds.Contains(s.RosterSpotId)).Select(s => ToEntry(s.PlayerId!.Value)),
                reserve = mine.Where(s => !activeIds.Contains(s.RosterSpotId)).Select(s => ToEntry(s.PlayerId!.Value)),
            };
        });

        var payload = new { source = "reset-mordus-rosters dump, season 20262027", activeSlots = new { forwards = 9, defense = 4, goalies = 1 }, teams = teamsJson };
        await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        Console.WriteLine($"Wrote {outFile}: {teams.Count} teams, {spots.Count} player spots.");
        return 0;
    }

    case "reset-mordus-rosters":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new ResetMordusRostersJob(db).RunAsync(
            file: GetOption(args, "--file") ?? "data/mordus-2026-27.json",
            dryRun: dryRun);
    }

    case "sim-clock":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        var clock = new SimulationClockService(db);
        if (args.Contains("--off"))
        {
            await clock.DisableAsync();
            Console.WriteLine("Simulation off — real clock.");
            return 0;
        }
        if (GetOption(args, "--set") is { } set)
            await clock.SetAsync(DateOnly.Parse(set), GetOption(args, "--season") ?? await CurrentSeasonAsync(db));
        var state = await clock.StateAsync();
        Console.WriteLine(state is null
            ? "No simulation running — real clock."
            : $"Simulated: asOfDate={state.AsOfDate:yyyy-MM-dd} season={state.Season} "
              + $"=> todayEt={await clock.TodayEtAsync():yyyy-MM-dd}");
        return 0;
    }

    case "sim-advance":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        if (GetOption(args, "--to") is not { } to)
        {
            Console.Error.WriteLine("--to YYYY-MM-DD is required.");
            return 1;
        }
        return await new SimAdvanceJob(db).RunAsync(DateOnly.Parse(to), dryRun);
    }

    default:
        Console.Error.WriteLine($"Unknown job \"{job}\".");
        return 1;
}
