using FantasyWarrior.Jobs.Nhl;
using Google.Cloud.Firestore;

// Usage: dotnet run -- <job> [options]
//   player-sync [--season 20262027]
//   salary-import --file <path.csv>   (columns: nhlId?,firstName,lastName,teamAbbrev,capHit)
//   stats-sync [--date YYYY-MM-DD | --from A --to B]   (default: yesterday UTC)
//   stats-check [--date YYYY-MM-DD]
//   score-calc [--league <leagueId>]
//   league-init-assignments
//   estimate-salaries [--season 20252026] [--top 200] [--top-max 14000000]
//                     [--top-min 3000000] [--default 1000000]
//     PLACEHOLDER cap hits (no real salary source wired up yet): scales
//     top-N real scorers (goals+assists) between top-min/top-max, flat
//     default for everyone else. Tags capHitSource="estimated".
//   set-league-cap --league <leagueId> --amount <dollars>   (0 clears the cap)
//   wipe-pools   (deletes all users/leagues/teams/assignments/adjustments; players/games/playerGameStats untouched)
//   seed-allstars [--league-name "Shemalz Pool"] [--season 20252026]
//                 [--forwards 4] [--defense 3] [--goalies 1]
//     Drafts top synced performers per position (snake draft) into 9 fixed
//     GMs, no adjustment ledger -- full season stats count as if always on
//     that team.
//   player-check
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
    case "stats-sync":
    {
        var single = GetOption(args, "--date");
        var from = GetOption(args, "--from") ?? single;
        var to = GetOption(args, "--to") ?? single;
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
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
    case "score-calc":
    {
        await new FantasyWarrior.Jobs.Scoring.ScoreCalcJob(db).RunAsync(GetOption(args, "--league"));
        return 0;
    }
    case "league-init-assignments":
    {
        // Migration: opens an "initial" assignment for rostered players that have none.
        var leagues = await db.Collection("leagues").GetSnapshotAsync();
        foreach (var leagueSnap in leagues.Documents)
        {
            var assignmentsCol = leagueSnap.Reference.Collection("assignments");
            var existing = (await assignmentsCol.WhereEqualTo("to", null).GetSnapshotAsync()).Documents
                .Select(d => (d.GetValue<long>("playerId"), d.GetValue<string>("teamUsername")))
                .ToHashSet();
            var created = 0;
            var fromDate = leagueSnap.GetValue<Timestamp>("createdUtc").ToDateTime().ToString("yyyy-MM-dd");
            foreach (var teamSnap in (await leagueSnap.Reference.Collection("teams").GetSnapshotAsync()).Documents)
            {
                var team = teamSnap.ConvertTo<FantasyWarrior.Core.Leagues.Team>();
                foreach (var playerId in team.PlayerIds.Where(p => !existing.Contains((p, team.OwnerUsername))))
                {
                    await assignmentsCol.AddAsync(new FantasyWarrior.Core.Leagues.Assignment
                    {
                        PlayerId = playerId,
                        TeamUsername = team.OwnerUsername,
                        From = fromDate,
                        Source = "initial",
                        CreatedUtc = Timestamp.GetCurrentTimestamp(),
                    });
                    created++;
                }
            }
            Console.WriteLine($"League [{leagueSnap.Id}]: {created} assignments created");
        }
        return 0;
    }
    case "seed-allstars":
    {
        var leagueName = GetOption(args, "--league-name") ?? "Shemalz Pool";
        var season = GetOption(args, "--season") ?? "20252026";
        var usernames = new[] { "jay", "nick", "al", "chuck", "baby", "sam", "dom", "vince", "didi" };
        const string commissioner = "nick";
        var forwardSlots = int.Parse(GetOption(args, "--forwards") ?? "4");
        var defenseSlots = int.Parse(GetOption(args, "--defense") ?? "3");
        var goalieSlots = int.Parse(GetOption(args, "--goalies") ?? "1");

        var now = Timestamp.GetCurrentTimestamp();
        foreach (var u in usernames)
        {
            var userDoc = db.Collection("users").Document(u);
            if (!(await userDoc.GetSnapshotAsync()).Exists)
                await userDoc.SetAsync(new FantasyWarrior.Core.Users.User { DisplayName = u, CreatedUtc = now, LastLoginUtc = now });
        }

        var leagueDoc = db.Collection("leagues").Document();
        await leagueDoc.SetAsync(new FantasyWarrior.Core.Leagues.League
        {
            Name = leagueName,
            Season = season,
            CommissionerUsername = commissioner,
            MemberUsernames = [.. usernames],
            RuleConfig = new FantasyWarrior.Core.Scoring.RuleConfig(),
            CreatedUtc = now,
        });

        // All lines for the season, filtered to regular season in memory
        // (avoids requiring a composite index for a two-field equality query).
        var linesSnap = await db.Collection("playerGameStats").WhereEqualTo("season", season).GetSnapshotAsync();
        var lines = linesSnap.Documents
            .Select(d => d.ConvertTo<FantasyWarrior.Core.Stats.PlayerGameStats>())
            .Where(l => l.GameType == 2)
            .ToList();
        var pointValues = new FantasyWarrior.Core.Scoring.RuleConfig().PointValues;

        var byPlayer = lines
            .GroupBy(l => l.PlayerId)
            .Select(g =>
            {
                var totals = FantasyWarrior.Core.Scoring.PlayerTotalsSource.Aggregate(g);
                return new
                {
                    PlayerId = g.Key,
                    Name = g.First().Name,
                    Group = FantasyWarrior.Core.Scoring.PositionGroups.From(g.First().Position),
                    Totals = totals,
                    FantasyPoints = FantasyWarrior.Core.Scoring.ScoringEngine.PlayerPoints(totals, pointValues),
                };
            })
            .ToList();

        // This full scan already computed every synced player's season totals —
        // persist them into the consolidated cache so nightly scoring and the
        // player-card API don't have to re-scan `playerGameStats` for these players.
        foreach (var chunk in byPlayer.Chunk(400))
        {
            var cacheBatch = db.StartBatch();
            foreach (var p in chunk)
                cacheBatch.Set(
                    db.Collection("playerSeasonStats").Document(FantasyWarrior.Core.Scoring.PlayerTotalsSource.SeasonStatsDocId(season, p.PlayerId)),
                    new FantasyWarrior.Core.Stats.PlayerSeasonStats
                    {
                        Season = season,
                        PlayerId = p.PlayerId,
                        GamesPlayed = p.Totals.GamesPlayed,
                        Goals = p.Totals.Goals,
                        Assists = p.Totals.Assists,
                        PlusMinus = p.Totals.PlusMinus,
                        Pim = p.Totals.Pim,
                        Shots = p.Totals.Shots,
                        Hits = p.Totals.Hits,
                        BlockedShots = p.Totals.BlockedShots,
                        Wins = p.Totals.Wins,
                        OtLosses = p.Totals.OtLosses,
                        Shutouts = p.Totals.Shutouts,
                        GoalsAgainst = p.Totals.GoalsAgainst,
                        Saves = p.Totals.Saves,
                        ShotsAgainst = p.Totals.ShotsAgainst,
                        UpdatedUtc = Timestamp.GetCurrentTimestamp(),
                    });
            await cacheBatch.CommitAsync();
        }
        Console.WriteLine($"Cached season totals for {byPlayer.Count} players in playerSeasonStats.");

        var forwards = byPlayer.Where(p => p.Group == FantasyWarrior.Core.Scoring.PositionGroup.Forward).OrderByDescending(p => p.FantasyPoints).ToList();
        var defense = byPlayer.Where(p => p.Group == FantasyWarrior.Core.Scoring.PositionGroup.Defense).OrderByDescending(p => p.FantasyPoints).ToList();
        var goalies = byPlayer.Where(p => p.Group == FantasyWarrior.Core.Scoring.PositionGroup.Goalie).OrderByDescending(p => p.FantasyPoints).ToList();

        var neededForwards = usernames.Length * forwardSlots;
        var neededDefense = usernames.Length * defenseSlots;
        var neededGoalies = usernames.Length * goalieSlots;
        if (forwards.Count < neededForwards || defense.Count < neededDefense || goalies.Count < neededGoalies)
        {
            Console.Error.WriteLine(
                $"Not enough synced players for season {season}: need {neededForwards}F/{neededDefense}D/{neededGoalies}G, " +
                $"have {forwards.Count}F/{defense.Count}D/{goalies.Count}G. Sync more stats first.");
            return 1;
        }
        Console.WriteLine(
            $"Pool: {forwards.Count}F (top: {forwards[0].Name} {forwards[0].FantasyPoints}pts), " +
            $"{defense.Count}D (top: {defense[0].Name} {defense[0].FantasyPoints}pts), {goalies.Count}G. " +
            $"Snake-drafting {forwardSlots}F + {defenseSlots}D + {goalieSlots}G per team.");

        var rosters = usernames.ToDictionary(u => u, _ => new List<long>());
        void SnakeDraft<T>(List<T> pool, int rounds, Func<T, long> id)
        {
            for (var round = 0; round < rounds && pool.Count > 0; round++)
            {
                var order = round % 2 == 0 ? usernames : usernames.Reverse().ToArray();
                foreach (var u in order)
                {
                    if (pool.Count == 0) break;
                    rosters[u].Add(id(pool[0]));
                    pool.RemoveAt(0);
                }
            }
        }
        SnakeDraft(forwards, forwardSlots, p => p.PlayerId);
        SnakeDraft(defense, defenseSlots, p => p.PlayerId);
        SnakeDraft(goalies, goalieSlots, p => p.PlayerId);

        foreach (var (username, playerIds) in rosters)
        {
            // Direct write, no adjustment ledger: full-season stats count as if
            // the player had always been on this team (no transaction to compensate for).
            await leagueDoc.Collection("teams").Document(username).SetAsync(new FantasyWarrior.Core.Leagues.Team
            {
                Name = $"Team {char.ToUpper(username[0])}{username[1..]}",
                OwnerUsername = username,
                PlayerIds = playerIds,
                CreatedUtc = now,
            });
            foreach (var pid in playerIds)
                await leagueDoc.Collection("assignments").AddAsync(new FantasyWarrior.Core.Leagues.Assignment
                {
                    PlayerId = pid,
                    TeamUsername = username,
                    From = $"{season[..4]}-10-01",
                    Source = "initial",
                    CreatedUtc = now,
                });
        }

        var rosterSize = forwardSlots + defenseSlots + goalieSlots;
        Console.WriteLine($"Seeded '{leagueName}' [{leagueDoc.Id}] season {season}: {usernames.Length} teams x {rosterSize} players ({forwardSlots}F/{defenseSlots}D/{goalieSlots}G), no adjustments.");
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
    case "wipe-pools":
    {
        var deletedUsers = await WipeCollectionAsync(db.Collection("users"));
        var leagues = await db.Collection("leagues").GetSnapshotAsync();
        var deletedLeagues = 0;
        foreach (var leagueSnap in leagues.Documents)
        {
            var teams = await leagueSnap.Reference.Collection("teams").GetSnapshotAsync();
            foreach (var teamDoc in teams.Documents)
                await WipeCollectionAsync(teamDoc.Reference.Collection("adjustments"));
            await WipeCollectionAsync(leagueSnap.Reference.Collection("teams"));
            await WipeCollectionAsync(leagueSnap.Reference.Collection("assignments"));
            await leagueSnap.Reference.DeleteAsync();
            deletedLeagues++;
        }
        Console.WriteLine($"wipe-pools: deleted {deletedUsers} users, {deletedLeagues} leagues (with their teams/assignments/adjustments). players/games/playerGameStats untouched.");
        return 0;
    }
    case "stats-check":
    {
        var date = GetOption(args, "--date") ?? DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1)).ToString("yyyy-MM-dd");
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
