using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Jobs.CapWages;
using FantasyWarrior.Jobs.News;
using FantasyWarrior.Jobs.Nhl;
using FantasyWarrior.Jobs.Ops;
using FantasyWarrior.Jobs.Sql;

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
//   period-init [--season 20252026] [--dry-run]
//     The weekly calendar, derived from the games we hold. Append-only:
//     moving a boundary would restate points teams already own.
//   nightly [--dry-run] [--backfill-from N]
//     THE nightly entry point, and the only place the correct order lives:
//     score the current week -> bank finished weeks -> execute accepted
//     trades effective next week.
//   period-rollup [--league <id>] [--week N] [--dry-run]
//     Scores one week into RosterAssignment rows. Everything above that grain
//     is a view, so this writes nothing else.
//
// --- league setup ---
//   seed-mordus [--file data/mordus-rosters.json] [--season] [--commissioner]
//               [--cap] [--dry-run] [--no-opening-lineup]
//     Creates "Les Mordus" from the rosters imported out of Nick's PoolExpert
//     PDF. --no-opening-lineup leaves week 1 to be auto-filled, which is what
//     the Firestore build did and the only setting under which a replay can be
//     compared against golden-scores-preSql.json.
//   wipe-pools [--dry-run]
//     Deletes pool data and un-banks every week. NHL reference data is
//     untouched -- that is the expensive half to rebuild.
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

static string CurrentSeason()
{
    var now = DateTime.UtcNow;
    var start = now.Month >= 9 ? now.Year : now.Year - 1;
    return $"{start}{start + 1}";
}

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
            .RunAsync(GetOption(args, "--season") ?? CurrentSeason(), dryRun);
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
            new NewsSource("rotowire_html", ct => rotowire.GetInjuryItemsAsync(rotoInj, ct), HasReliablePublishedDate: false),
            new NewsSource("fantasysp", ct => fantasySp.GetInjuryItemsAsync(fspUrl, ct), HasReliablePublishedDate: false),
        ]);
    }

    case "period-init":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new PeriodInitJob(db).RunAsync(GetOption(args, "--season") ?? "20252026", dryRun);
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
            season: GetOption(args, "--season") ?? "20252026",
            commissioner: GetOption(args, "--commissioner") ?? "nick",
            capAmount: long.TryParse(GetOption(args, "--cap"), out var cap) ? cap : 115_000_000,
            dryRun: dryRun,
            openingLineup: !args.Contains("--no-opening-lineup"));
    }

    case "wipe-pools":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new WipePoolsJob(db).RunAsync(dryRun);
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
            await clock.SetAsync(DateOnly.Parse(set), GetOption(args, "--season") ?? "20252026");
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
