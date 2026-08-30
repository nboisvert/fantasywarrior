using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Seasons;
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
//   rules-backfill [--league <joinCode>] [--force] [--dry-run]
//     Writes every LeagueSeason's rules document from the columns that used to
//     hold them. Run once after the migration that adds the column: until it
//     has, a league's rules read as "never written" and every consumer refuses
//     rather than serving a configuration nobody chose. Only fills blanks;
//     --force overwrites documents already written.
//   seed-mordus [--file data/mordus-rosters.json] [--season] [--commissioner]
//               [--cap] [--dry-run] [--no-opening-lineup]
//     Creates "Les Mordus" from the rosters imported out of Nick's PoolExpert
//     PDF. --no-opening-lineup leaves week 1 to be auto-filled, which is what
//     the Firestore build did and the only setting under which a replay can be
//     compared against golden-scores-preSql.json.
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

    case "rules-backfill":
    {
        await using var db = DataServiceCollectionExtensions.CreateContext();
        return await new RulesBackfillJob(db).RunAsync(
            GetOption(args, "--league"), args.Contains("--force"), dryRun);
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
            openingLineup: !args.Contains("--no-opening-lineup"));
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
