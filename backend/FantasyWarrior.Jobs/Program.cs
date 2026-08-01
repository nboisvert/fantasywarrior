using FantasyWarrior.Core.Time;
using FantasyWarrior.Jobs.News;
using FantasyWarrior.Jobs.Nhl;
using Google.Cloud.Firestore;

// Usage: dotnet run -- <job> [options]
//
// --- season simulation (test mode) ---
//   sim-clock [--set YYYY-MM-DD] [--season 20252026] [--off]
//     Reads or moves the simulation cursor (`simulation/clock`). asOfDate is
//     the last game day whose results are known, so todayEt = asOfDate + 1.
//     Every reader -- jobs, local API, deployed API -- takes its "today" from
//     this one document. --off returns everything to the real clock.
//
// --- nightly pipeline ---
//   nightly [--shadow] [--dry-run] [--backfill-from N]
//     THE nightly entry point, and the only place the correct order lives:
//     score the current week -> bank finished weeks -> execute accepted trades
//     effective next week -> materialize next week's lineups. Trades must land
//     after banking and before materialization; as separate workflow steps
//     nothing enforced that. --shadow leaves the `score` field untouched.
//     --backfill-from replays weeks N..current, scoring and banking each in
//     turn (rollups only accumulate at banking, so weeks can't be done in
//     bulk). One date-range query per week -- a full season is ~50k reads, a
//     deliberate operation.
//   stats-sync [--date YYYY-MM-DD | --from A --to B]   (default: ET yesterday)
//   player-sync [--season 20262027]
//   draft-sync
//     Backfills NHL entry draft details via the per-player landing endpoint --
//     one HTTP call per player, hence separate from the roster-based sync.
//   news-sync [--rotowire-url <url>] [--rotowire-injuries-url <url>] [--fantasysp-url <url>]
//     NHL news into the global `news` collection from Rotowire's RSS feed,
//     its injuries page, and FantasySP's injuries table (no RSS exists for
//     that one). Personal/non-commercial use only per both sites' terms.
//
// --- scoring internals (the nightly job calls these) ---
//   period-rollup [--league <id>] [--commit-score] [--dry-run]
//     Scores one week and rolls it up lineup -> roster spot -> team. One
//     date-range query serves every league, replacing the old per-player
//     career scans. Shadow by default.
//   recompute --season <s> [--from N] [--dry-run]
//     Un-banks weeks N and later so they can be scored again -- the escape
//     hatch for a scoring bug, a mid-season rule change, or a week banked at
//     zero before its lineups existed. Banked points are otherwise immutable
//     by design. Follow with `nightly --backfill-from N`.
//   period-init [--season 20252026] [--dry-run]
//     Generates the season's weekly calendar (Mon-Sun ET) into the global
//     `periods` collection, derived from the `games` we already hold -- no
//     NHL API call. Append-only: moving a boundary would restate banked points.
//   period-lock --season <s> --index <n> --utc <ISO8601>
//     Moves one week's lineup lock, for a real schedule change or to test the
//     lineup endpoints against a week that isn't locked yet.
//
// --- league setup ---
//   seed-mordus [--file data/mordus-rosters.json] [--season 20252026]
//               [--commissioner nick] [--cap 115000000] [--dry-run]
//     Creates the real "Les Mordus" league from the rosters imported out of
//     Nick's PoolExpert PDF (see .claude/doc/mordus-pool.md). Verifies every
//     player id before writing; refuses to overwrite an existing league.
//   backfill-roster-spots [--league <id>] [--start-date YYYY-MM-DD] [--dry-run]
//     Opens one roster spot per rostered player, for leagues seeded straight
//     into team.playerIds. Spots start at the season's first period, not the
//     league's creation date -- otherwise every scoring window falls before
//     they existed and every team scores zero.
//   set-league-rules --league <id> [--goal N] [--assist N] [--goalie-win N]
//                    [--goalie-otl N] [--shutout N] [--forwards N] [--defense N]
//                    [--goalies N] [--roster-min N] [--roster-max N] [--cap N]
//     Commissioner config from the CLI. Only the options passed are changed.
//   set-league-cap --league <id> --amount <dollars>   (0 clears the cap)
//   salary-import --file <path.csv>   (nhlId?,firstName,lastName,teamAbbrev,capHit)
//   estimate-salaries [--season 20252026] [--top 200] [--top-max 14000000]
//                     [--top-min 3000000] [--default 1000000]
//     PLACEHOLDER cap hits -- no real salary source is wired up. Tags
//     capHitSource="estimated" so it is never mistaken for contract data.
//   seed-trades --league <id>
//     Wipes and reseeds one trade of each status for demo purposes.
//   reseed-demo [--league-name "Shemalz Pool"] [--season 20252026]
//     One-command demo rebuild: wipes pool data (never players/games/
//     playerGameStats), recreates the league, drafts from real top performers,
//     estimates salaries, seeds trades and scores the current week.
//   wipe-pools
//     Deletes users + leagues and everything under them. NHL reference data
//     (players/games/playerGameStats/playerSeasonStats/news) is untouched.
//
// --- diagnostics ---
//   check-indexes [--create]
//     Probes every composite-index-requiring query with Limit(1). MUST run
//     against real Firestore -- the emulator ignores composite indexes and
//     would report success for everything. --create makes the missing ones
//     via the Admin API (needs roles/datastore.indexAdmin).
//   stats-check [--date YYYY-MM-DD]
//   player-check
//   player-dump [--out players.json]
//
// Firestore target: set FIRESTORE_EMULATOR_HOST (e.g. localhost:8090) for local dev;
// otherwise GOOGLE_APPLICATION_CREDENTIALS + FIRESTORE_PROJECT_ID for production.

var job = args.FirstOrDefault();
if (job is null)
{
    Console.Error.WriteLine("Usage: FantasyWarrior.Jobs <player-sync> [options]");
    return 1;
}

var projectId = Environment.GetEnvironmentVariable("FIRESTORE_PROJECT_ID")
    ?? (Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") is not null
        ? "demo-fantasy-warrior"
        : null);
if (projectId is null)
{
    Console.Error.WriteLine("Set FIRESTORE_PROJECT_ID (or FIRESTORE_EMULATOR_HOST for local dev).");
    return 1;
}

var db = await new FirestoreDbBuilder
{
    ProjectId = projectId,
    EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOrProduction,
}.BuildAsync();

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("FantasyWarrior/0.1");

switch (job)
{
    case "player-sync":
    {
        var season = GetOption(args, "--season") ?? CurrentSeason();
        await new PlayerSyncJob(new NhlApiClient(http), db).RunAsync(season);
        return 0;
    }
    case "draft-sync":
    {
        await new DraftSyncJob(new NhlApiClient(http), db).RunAsync();
        return 0;
    }
    case "stats-sync":
    {
        var single = GetOption(args, "--date");
        var from = GetOption(args, "--from") ?? single;
        var to = GetOption(args, "--to") ?? single;
        // ET yesterday, not UTC yesterday: at the scheduled 09:30 UTC the two
        // coincide, but a manual/retry run before 05:00 UTC would sync the
        // wrong day entirely.
        var yesterday = PoolClock.LastStatDate(DateTimeOffset.UtcNow);
        var fromDate = from is null ? yesterday : DateOnly.Parse(from);
        var toDate = to is null ? yesterday : DateOnly.Parse(to);
        if (toDate < fromDate)
        {
            Console.Error.WriteLine("--to must be >= --from");
            return 1;
        }
        Console.WriteLine($"StatsSync: {fromDate:yyyy-MM-dd} -> {toDate:yyyy-MM-dd}");
        await new StatsSyncJob(new NhlApiClient(http), db).RunAsync(fromDate, toDate);
        return 0;
    }
    case "news-sync":
    {
        // Source names/schema follow the integration guide's normalized
        // "source" values (rotowire_rss | rotowire_html | fantasysp).
        // Rotowire's RSS feed is real but can be quiet off-season and only
        // carries plain-fact headlines; rotowire_html scrapes its richer
        // injuries page (headline/position/team) as a supplement, not a
        // replacement. FantasySP has no public RSS at all (confirmed live:
        // no /rss endpoint exists), so it's HTML-scraped exclusively. Both
        // HTML sources are flagged as not having a reliable per-item
        // published date (no per-item date field was found in either
        // site's captured structure) — see NewsSource's doc comment for
        // why that matters.
        var rotowireRssUrl = GetOption(args, "--rotowire-url") ?? "https://www.rotowire.com/rss/news.php?sport=NHL";
        var rotowireInjuriesUrl = GetOption(args, "--rotowire-injuries-url") ?? "https://www.rotowire.com/hockey/news.php?view=injuries";
        var fantasySpUrl = GetOption(args, "--fantasysp-url") ?? "https://www.fantasysp.com/injuries/nhl/";
        var rss = new RssNewsClient(http);
        var rotowireHtml = new RotowireInjuryScraper(http);
        var fantasySpScraper = new FantasySpScraper(http);
        var sources = new[]
        {
            new NewsSource("rotowire_rss", ct => rss.GetItemsAsync(rotowireRssUrl, ct), HasReliablePublishedDate: true),
            new NewsSource("rotowire_html", ct => rotowireHtml.GetInjuryItemsAsync(rotowireInjuriesUrl, ct), HasReliablePublishedDate: false),
            new NewsSource("fantasysp", ct => fantasySpScraper.GetInjuryItemsAsync(fantasySpUrl, ct), HasReliablePublishedDate: false),
        };
        await new NewsSyncJob(db).RunAsync(sources);
        return 0;
    }
    case "process-trades":
    {
        await new FantasyWarrior.Jobs.Trades.ProcessTradesJob(db).RunAsync();
        return 0;
    }
    case "seed-trades":
    {
        var seedLeagueId = GetOption(args, "--league");
        if (seedLeagueId is null)
        {
            Console.Error.WriteLine("--league is required.");
            return 1;
        }
        await new FantasyWarrior.Jobs.Trades.SeedTradesJob(db).RunAsync(seedLeagueId);
        return 0;
    }
    case "reseed-demo":
    {
        var reseedLeagueName = GetOption(args, "--league-name") ?? "Shemalz Pool";
        var reseedSeason = GetOption(args, "--season") ?? "20252026";
        await new FantasyWarrior.Jobs.Demo.FullReseedJob(db).RunAsync(reseedLeagueName, reseedSeason);
        return 0;
    }
    case "estimate-salaries":
    {
        // Placeholder cap hits until a real salary source is wired up (PuckPedia
        // is private/paid; no free bulk source exists). Nick approved this stopgap
        // 2026-07-22: scale $topMin-$topMax across the top N scorers by real
        // points (goals+assists), flat $default for everyone else. Every
        // written doc is tagged capHitSource="estimated" so it's never
        // mistaken for real contract data later.
        var season = GetOption(args, "--season") ?? "20252026";
        var topCount = int.Parse(GetOption(args, "--top") ?? "200");
        var topMax = long.Parse(GetOption(args, "--top-max") ?? "14000000");
        var topMin = long.Parse(GetOption(args, "--top-min") ?? "3000000");
        var defaultSalary = long.Parse(GetOption(args, "--default") ?? "1000000");

        var seasonStatsSnap = await db.Collection("playerSeasonStats").WhereEqualTo("season", season).GetSnapshotAsync();
        var ranked = seasonStatsSnap.Documents
            .Select(d => d.ConvertTo<FantasyWarrior.Core.Stats.PlayerSeasonStats>())
            .OrderByDescending(s => s.Goals + s.Assists)
            .ToList();

        var salaryByPlayerId = new Dictionary<long, long>();
        for (var i = 0; i < ranked.Count && i < topCount; i++)
        {
            var t = topCount > 1 ? (double)i / (topCount - 1) : 0;
            salaryByPlayerId[ranked[i].PlayerId] = (long)Math.Round(topMax - t * (topMax - topMin));
        }

        var allPlayers = await db.Collection("players").GetSnapshotAsync();
        var now = Timestamp.GetCurrentTimestamp();
        var updated = 0;
        foreach (var chunk in allPlayers.Documents.Chunk(400))
        {
            var batch = db.StartBatch();
            foreach (var doc in chunk)
            {
                var salary = salaryByPlayerId.GetValueOrDefault(long.Parse(doc.Id), defaultSalary);
                batch.Update(doc.Reference, new Dictionary<string, object>
                {
                    ["capHit"] = salary,
                    ["capHitSource"] = "estimated",
                    ["capHitUpdatedUtc"] = now,
                });
                updated++;
            }
            await batch.CommitAsync();
        }
        Console.WriteLine(
            $"estimate-salaries: {salaryByPlayerId.Count} top scorers scaled ${topMin:N0}-${topMax:N0}, " +
            $"{updated - salaryByPlayerId.Count} others set to ${defaultSalary:N0}. Total: {updated} players.");
        return 0;
    }
    case "set-league-cap":
    {
        var leagueId = GetOption(args, "--league");
        var amountStr = GetOption(args, "--amount");
        if (leagueId is null || amountStr is null || !long.TryParse(amountStr, out var amount))
        {
            Console.Error.WriteLine("Usage: set-league-cap --league <leagueId> --amount <dollars> (0 clears the cap)");
            return 1;
        }
        var leagueDoc = db.Collection("leagues").Document(leagueId);
        if (!(await leagueDoc.GetSnapshotAsync()).Exists)
        {
            Console.Error.WriteLine($"League {leagueId} not found.");
            return 1;
        }
        if (amount == 0)
            await leagueDoc.UpdateAsync("capAmount", FieldValue.Delete);
        else
            await leagueDoc.UpdateAsync("capAmount", amount);
        Console.WriteLine($"League [{leagueId}]: capAmount set to {(amount == 0 ? "null (no cap)" : amount.ToString())}.");
        return 0;
    }
    case "set-league-rules":
    {
        var leagueId = GetOption(args, "--league");
        if (leagueId is null)
        {
            Console.Error.WriteLine("Usage: set-league-rules --league <leagueId> [--goal N] [--assist N] "
                + "[--goalie-win N] [--goalie-otl N] [--shutout N] [--roster-min N] [--roster-max N] [--cap N]");
            return 1;
        }
        var leagueDoc = db.Collection("leagues").Document(leagueId);
        var leagueSnap = await leagueDoc.GetSnapshotAsync();
        if (!leagueSnap.Exists)
        {
            Console.Error.WriteLine($"League {leagueId} not found.");
            return 1;
        }
        var league = leagueSnap.ConvertTo<FantasyWarrior.Core.Leagues.League>();
        var rules = league.RuleConfig;

        // Only the options actually passed are changed -- everything else keeps
        // its current value, so this is safe to run for a single tweak.
        double? D(string o) => double.TryParse(GetOption(args, o), out var v) ? v : null;
        int? I(string o) => int.TryParse(GetOption(args, o), out var v) ? v : null;
        rules.PointValues.Goal = D("--goal") ?? rules.PointValues.Goal;
        rules.PointValues.Assist = D("--assist") ?? rules.PointValues.Assist;
        rules.PointValues.GoalieWin = D("--goalie-win") ?? rules.PointValues.GoalieWin;
        rules.PointValues.GoalieOtLoss = D("--goalie-otl") ?? rules.PointValues.GoalieOtLoss;
        rules.PointValues.Shutout = D("--shutout") ?? rules.PointValues.Shutout;
        rules.RosterSize.Min = I("--roster-min") ?? rules.RosterSize.Min;
        rules.RosterSize.Max = I("--roster-max") ?? rules.RosterSize.Max;
        // Active lineup slots per position. Still stored under `topCount` --
        // the field is renamed to `activeSlots` when RuleConfig moves to a map.
        rules.TopCount.Forwards = I("--forwards") ?? rules.TopCount.Forwards;
        rules.TopCount.Defense = I("--defense") ?? rules.TopCount.Defense;
        rules.TopCount.Goalies = I("--goalies") ?? rules.TopCount.Goalies;

        await leagueDoc.UpdateAsync("ruleConfig", rules);
        if (I("--cap") is not null && long.TryParse(GetOption(args, "--cap"), out var newCap))
            await leagueDoc.UpdateAsync("capAmount", newCap);

        var updated = (await leagueDoc.GetSnapshotAsync()).ConvertTo<FantasyWarrior.Core.Leagues.League>();
        var pv = updated.RuleConfig.PointValues;
        Console.WriteLine($"League [{leagueId}] '{updated.Name}':");
        Console.WriteLine($"  points   goal={pv.Goal} assist={pv.Assist} goalieWin={pv.GoalieWin} goalieOtLoss={pv.GoalieOtLoss} shutout={pv.Shutout}");
        var tc = updated.RuleConfig.TopCount;
        Console.WriteLine($"  lineup   {tc.Forwards?.ToString() ?? "-"}F / {tc.Defense?.ToString() ?? "-"}D / {tc.Goalies?.ToString() ?? "-"}G active");
        Console.WriteLine($"  roster   min={updated.RuleConfig.RosterSize.Min?.ToString() ?? "-"} max={updated.RuleConfig.RosterSize.Max?.ToString() ?? "-"}");
        Console.WriteLine($"  cap      {updated.CapAmount?.ToString("N0") ?? "none"}");
        Console.WriteLine("  Scores refresh at the next nightly calculation (or run score-calc).");
        return 0;
    }
    case "wipe-pools":
    {
        var deletedUsers = await WipeCollectionAsync(db.Collection("users"));
        var leagues = await db.Collection("leagues").GetSnapshotAsync();
        var deletedLeagues = 0;
        foreach (var leagueSnap in leagues.Documents)
        {
            await WipeCollectionAsync(leagueSnap.Reference.Collection("teams"));
            await WipeCollectionAsync(leagueSnap.Reference.Collection("rosterSpots"));
            await WipeCollectionAsync(leagueSnap.Reference.Collection("lineups"));
            var trades = await leagueSnap.Reference.Collection("trades").GetSnapshotAsync();
            foreach (var tradeDoc in trades.Documents)
                await WipeCollectionAsync(tradeDoc.Reference.Collection("votes"));
            await WipeCollectionAsync(leagueSnap.Reference.Collection("trades"));
            await leagueSnap.Reference.DeleteAsync();
            deletedLeagues++;
        }
        Console.WriteLine($"wipe-pools: deleted {deletedUsers} users, {deletedLeagues} leagues "
            + "(teams/rosterSpots/lineups/trades). NHL reference data untouched.");
        return 0;
    }
    case "sim-clock":
    {
        var clock = new SimulationClock(db);
        if (args.Contains("--off"))
        {
            await clock.DisableAsync();
            Console.WriteLine("Simulation disabled — everything is back on the real clock.");
            return 0;
        }
        if (GetOption(args, "--set") is { } setTo)
        {
            if (!DateOnly.TryParse(setTo, out var parsed))
            {
                Console.Error.WriteLine("--set expects YYYY-MM-DD.");
                return 1;
            }
            await clock.SetAsync(PoolClock.Iso(parsed), GetOption(args, "--season") ?? "20252026");
        }
        var state = await clock.StateAsync();
        var simNow = await clock.NowAsync();
        Console.WriteLine(state is null
            ? "No simulation running — real clock."
            : $"SIMULATED  season {state.Season}  asOfDate {state.AsOfDate}");
        Console.WriteLine($"  todayEt       {PoolClock.TodayEtIso(simNow)}");
        Console.WriteLine($"  lastStatDate  {PoolClock.LastStatDateIso(simNow)}");
        return 0;
    }
    case "nightly":
        return await new FantasyWarrior.Jobs.Nightly.NightlyJob(db).RunAsync(
            commitScore: !args.Contains("--shadow"),
            dryRun: args.Contains("--dry-run"),
            backfillFrom: int.TryParse(GetOption(args, "--backfill-from"), out var bf) ? bf : null);
    case "recompute":
        return await new FantasyWarrior.Jobs.Scoring.RecomputeJob(db).RunAsync(
            season: GetOption(args, "--season") ?? "20252026",
            fromPeriod: int.TryParse(GetOption(args, "--from"), out var rf) ? rf : 1,
            dryRun: args.Contains("--dry-run"));
    case "period-lock":
    {
        var season = GetOption(args, "--season") ?? "20252026";
        var utc = GetOption(args, "--utc");
        if (!int.TryParse(GetOption(args, "--index"), out var idx) || utc is null)
        {
            Console.Error.WriteLine("Usage: period-lock --season <s> --index <n> --utc <ISO8601>");
            return 1;
        }
        var pid = FantasyWarrior.Core.Periods.PeriodId.For(season, idx);
        var pref = db.Collection("periods").Document(pid);
        if (!(await pref.GetSnapshotAsync()).Exists)
        {
            Console.Error.WriteLine($"Period {pid} not found.");
            return 1;
        }
        var lockAt = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        await pref.UpdateAsync("lockUtc", Timestamp.FromDateTime(lockAt));
        Console.WriteLine($"{pid}: lockUtc set to {lockAt:yyyy-MM-dd HH:mm:ss}Z.");
        return 0;
    }
    case "period-rollup":
        return await new FantasyWarrior.Jobs.Scoring.PeriodRollupJob(db).RunAsync(
            onlyLeagueId: GetOption(args, "--league"),
            commitScore: args.Contains("--commit-score"),
            dryRun: args.Contains("--dry-run"));
    case "backfill-roster-spots":
        return await new FantasyWarrior.Jobs.Leagues.BackfillRosterSpotsJob(db).RunAsync(
            onlyLeagueId: GetOption(args, "--league"),
            startDateOverride: GetOption(args, "--start-date"),
            dryRun: args.Contains("--dry-run"));
    case "period-init":
        return await new FantasyWarrior.Jobs.Periods.PeriodInitJob(db).RunAsync(
            season: GetOption(args, "--season") ?? "20252026",
            dryRun: args.Contains("--dry-run"));
    case "seed-mordus":
    {
        await new FantasyWarrior.Jobs.Demo.SeedMordusJob(db).RunAsync(
            file: GetOption(args, "--file") ?? "data/mordus-rosters.json",
            season: GetOption(args, "--season") ?? "20252026",
            commissioner: GetOption(args, "--commissioner") ?? "nicolas",
            capAmount: long.TryParse(GetOption(args, "--cap"), out var mordusCap) ? mordusCap : 100_000_000,
            dryRun: args.Contains("--dry-run"));
        return 0;
    }
    case "check-indexes":
        return await new FantasyWarrior.Jobs.Ops.CheckIndexesJob(db).RunAsync(create: args.Contains("--create"));
    case "stats-check":
    {
        var date = GetOption(args, "--date") ?? PoolClock.LastStatDateIso(DateTimeOffset.UtcNow);
        var games = await db.Collection("games").WhereEqualTo("date", date).GetSnapshotAsync();
        var lines = await db.Collection("playerGameStats").WhereEqualTo("date", date).GetSnapshotAsync();
        Console.WriteLine($"{date}: {games.Count} games, {lines.Count} player lines");
        foreach (var g in games.Documents.Take(5))
            Console.WriteLine($"  [{g.Id}] {g.GetValue<string>("awayAbbrev")} {g.GetValue<int>("awayScore")} @ {g.GetValue<string>("homeAbbrev")} {g.GetValue<int>("homeScore")} ({g.GetValue<string>("lastPeriodType")})");
        var top = lines.Documents
            .Select(d => d.ConvertTo<FantasyWarrior.Core.Stats.PlayerGameStats>())
            .Where(l => !l.IsGoalie)
            .OrderByDescending(l => l.Points)
            .Take(3);
        foreach (var l in top)
            Console.WriteLine($"  top: {l.Name} ({l.TeamAbbrev}) {l.Goals}G {l.Assists}A vs {l.OpponentAbbrev}");
        var goalies = lines.Documents
            .Select(d => d.ConvertTo<FantasyWarrior.Core.Stats.PlayerGameStats>())
            .Where(l => l.IsGoalie && (l.Shutout == true || l.OtLoss == true));
        foreach (var l in goalies)
            Console.WriteLine($"  goalie: {l.Name} ({l.TeamAbbrev}) GA={l.GoalsAgainst} {l.Decision}{(l.Shutout == true ? " SHUTOUT" : "")}{(l.OtLoss == true ? " OTL" : "")}");
        return 0;
    }
    case "salary-import":
    {
        var file = GetOption(args, "--file");
        if (file is null || !File.Exists(file))
        {
            Console.Error.WriteLine("Usage: salary-import --file <path.csv>");
            return 1;
        }
        await new FantasyWarrior.Jobs.Salaries.SalaryImportJob(db).RunAsync(file);
        return 0;
    }
    case "player-dump":
    {
        // Dumps the players collection to JSON for offline work (name matching,
        // roster imports). Read-only; the file is a snapshot, never a source of truth.
        var outPath = GetOption(args, "--out") ?? "players.json";
        var snap = await db.Collection("players").GetSnapshotAsync();
        var rows = snap.Documents
            .Select(d => d.ConvertTo<FantasyWarrior.Core.Players.Player>())
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Select(p => new
            {
                id = p.NhlId,
                name = $"{p.FirstName} {p.LastName}",
                pos = p.Position,
                team = p.TeamAbbrev,
                status = p.Status,
                capHit = p.CapHit,
            });
        await File.WriteAllTextAsync(outPath,
            System.Text.Json.JsonSerializer.Serialize(rows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"player-dump: wrote {snap.Count} players to {outPath}");
        return 0;
    }
    case "player-check":
    {
        var players = db.Collection("players");
        var count = (await players.Count().GetSnapshotAsync()).Count;
        var sample = await players.WhereEqualTo("teamAbbrev", "MTL").Limit(3).GetSnapshotAsync();
        Console.WriteLine($"players collection: {count} documents");
        foreach (var doc in sample.Documents)
        {
            var p = doc.ConvertTo<FantasyWarrior.Core.Players.Player>();
            Console.WriteLine($"  [{doc.Id}] {p.FirstName} {p.LastName} ({p.Position}, {p.TeamAbbrev}, {p.Status}) capHit={p.CapHit?.ToString() ?? "null"}");
        }
        return 0;
    }
    default:
        Console.Error.WriteLine($"Unknown job '{job}'.");
        return 1;
}

static string? GetOption(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static async Task<int> WipeCollectionAsync(CollectionReference col)
{
    var snapshot = await col.GetSnapshotAsync();
    var count = 0;
    foreach (var chunk in snapshot.Documents.Chunk(500))
    {
        var batch = col.Database.StartBatch();
        foreach (var doc in chunk)
            batch.Delete(doc.Reference);
        await batch.CommitAsync();
        count += chunk.Length;
    }
    return count;
}

// NHL seasons roll over in July (post-draft/free agency).
static string CurrentSeason()
{
    var today = DateTime.UtcNow;
    var startYear = today.Month >= 7 ? today.Year : today.Year - 1;
    return $"{startYear}{startYear + 1}";
}
