using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Stats;
using FantasyWarrior.Core.Users;
using FantasyWarrior.Jobs.Trades;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Demo;

/// <summary>
/// One-command full reseed of the demo pool: wipes every pool doc (users,
/// leagues, teams, assignments, adjustments, trades/votes — never touches
/// `players`/`games`/`playerGameStats`, the real NHL data), then rebuilds
/// "Shemalz Pool" from scratch — rosters drafted from real top performers
/// per the league's own current rules, real-looking salaries, a demo trade
/// set spanning every status (including one fresh enough to trigger the
/// news ticker's hot alert), and a final score-calc so everything is
/// consistent the moment it's done.
/// </summary>
public sealed class FullReseedJob(FirestoreDb db)
{
    private static readonly string[] Usernames = ["jay", "nick", "al", "chuck", "baby", "sam", "dom", "vince", "didi"];
    private const string Commissioner = "nick";

    public async Task RunAsync(string leagueName = "Shemalz Pool", string season = "20252026", CancellationToken ct = default)
    {
        // Capture the current league's rules/cap (if it exists) before
        // wiping, so the reseed honors "the league's actual current rules"
        // instead of silently resetting to hardcoded defaults.
        var existingLeagueSnap = (await db.Collection("leagues").WhereEqualTo("name", leagueName).Limit(1).GetSnapshotAsync(ct))
            .Documents.FirstOrDefault();
        var ruleConfig = existingLeagueSnap?.ConvertTo<League>().RuleConfig ?? new RuleConfig();
        var capAmount = existingLeagueSnap?.ConvertTo<League>().CapAmount ?? 100_000_000;
        var forwardSlots = ruleConfig.TopCount.Forwards ?? 4;
        var defenseSlots = ruleConfig.TopCount.Defense ?? 3;
        var goalieSlots = ruleConfig.TopCount.Goalies ?? 1;

        Console.WriteLine($"=== Full reseed: '{leagueName}' season {season} ===");
        Console.WriteLine($"Rules carried over: {forwardSlots}F/{defenseSlots}D/{goalieSlots}G roster, ${capAmount:N0} cap.");

        await WipeAllPoolDataAsync(ct);

        var leagueDoc = await CreateUsersAndLeagueAsync(leagueName, season, ruleConfig, capAmount, ct);
        var byPlayer = await CacheSeasonTotalsAsync(season, ct);
        await DraftRostersAsync(leagueDoc, byPlayer, forwardSlots, defenseSlots, goalieSlots, ct);
        await EstimateSalariesAsync(season, ct);

        Console.WriteLine("--- Seeding demo trades (pending/accepted/declined/processed-and-hot) ---");
        await new SeedTradesJob(db).RunAsync(leagueDoc.Id, ct);

        Console.WriteLine("--- Running score-calc ---");
        await new FantasyWarrior.Jobs.Scoring.ScoreCalcJob(db).RunAsync(leagueDoc.Id, ct);

        Console.WriteLine($"=== Done. League id: {leagueDoc.Id} ===");
    }

    private async Task WipeAllPoolDataAsync(CancellationToken ct)
    {
        var deletedUsers = await WipeCollectionAsync(db.Collection("users"), ct);
        var leagues = await db.Collection("leagues").GetSnapshotAsync(ct);
        foreach (var leagueSnap in leagues.Documents)
        {
            var teams = await leagueSnap.Reference.Collection("teams").GetSnapshotAsync(ct);
            foreach (var teamDoc in teams.Documents)
                await WipeCollectionAsync(teamDoc.Reference.Collection("adjustments"), ct);
            await WipeCollectionAsync(leagueSnap.Reference.Collection("teams"), ct);
            await WipeCollectionAsync(leagueSnap.Reference.Collection("assignments"), ct);
            var trades = await leagueSnap.Reference.Collection("trades").GetSnapshotAsync(ct);
            foreach (var tradeDoc in trades.Documents)
                await WipeCollectionAsync(tradeDoc.Reference.Collection("votes"), ct);
            await WipeCollectionAsync(leagueSnap.Reference.Collection("trades"), ct);
            await leagueSnap.Reference.DeleteAsync(cancellationToken: ct);
        }
        Console.WriteLine($"Wiped {deletedUsers} users and {leagues.Count} league(s) (teams/assignments/adjustments/trades). NHL data untouched.");
    }

    private async Task<DocumentReference> CreateUsersAndLeagueAsync(
        string leagueName, string season, RuleConfig ruleConfig, long capAmount, CancellationToken ct)
    {
        var now = Timestamp.GetCurrentTimestamp();
        foreach (var u in Usernames)
            await db.Collection("users").Document(u).SetAsync(new User { DisplayName = u, CreatedUtc = now, LastLoginUtc = now }, cancellationToken: ct);

        var leagueDoc = db.Collection("leagues").Document();
        await leagueDoc.SetAsync(new League
        {
            Name = leagueName,
            Season = season,
            CommissionerUsername = Commissioner,
            MemberUsernames = [.. Usernames],
            RuleConfig = ruleConfig,
            CapAmount = capAmount,
            CreatedUtc = now,
        }, cancellationToken: ct);
        return leagueDoc;
    }

    private sealed record RankedPlayer(long PlayerId, string Name, PositionGroup Group, PlayerRawTotals Totals, double FantasyPoints);

    private async Task<List<RankedPlayer>> CacheSeasonTotalsAsync(string season, CancellationToken ct)
    {
        // All lines for the season, filtered to regular season in memory
        // (avoids requiring a composite index for a two-field equality query)
        // — same approach as seed-allstars.
        var linesSnap = await db.Collection("playerGameStats").WhereEqualTo("season", season).GetSnapshotAsync(ct);
        var lines = linesSnap.Documents.Select(d => d.ConvertTo<PlayerGameStats>()).Where(l => l.GameType == 2).ToList();
        var pointValues = new RuleConfig().PointValues;

        var byPlayer = lines
            .GroupBy(l => l.PlayerId)
            .Select(g =>
            {
                var totals = PlayerTotalsSource.Aggregate(g);
                return new RankedPlayer(g.Key, g.First().Name, PositionGroups.From(g.First().Position), totals, ScoringEngine.PlayerPoints(totals, pointValues));
            })
            .ToList();

        foreach (var chunk in byPlayer.Chunk(400))
        {
            var batch = db.StartBatch();
            foreach (var p in chunk)
                batch.Set(
                    db.Collection("playerSeasonStats").Document(PlayerTotalsSource.SeasonStatsDocId(season, p.PlayerId)),
                    new PlayerSeasonStats
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
            await batch.CommitAsync(ct);
        }
        Console.WriteLine($"Cached season totals for {byPlayer.Count} players in playerSeasonStats.");
        return byPlayer;
    }

    private async Task DraftRostersAsync(
        DocumentReference leagueDoc, List<RankedPlayer> byPlayer, int forwardSlots, int defenseSlots, int goalieSlots, CancellationToken ct)
    {
        var forwards = byPlayer.Where(p => p.Group == PositionGroup.Forward).OrderByDescending(p => p.FantasyPoints).ToList();
        var defense = byPlayer.Where(p => p.Group == PositionGroup.Defense).OrderByDescending(p => p.FantasyPoints).ToList();
        var goalies = byPlayer.Where(p => p.Group == PositionGroup.Goalie).OrderByDescending(p => p.FantasyPoints).ToList();

        var neededForwards = Usernames.Length * forwardSlots;
        var neededDefense = Usernames.Length * defenseSlots;
        var neededGoalies = Usernames.Length * goalieSlots;
        if (forwards.Count < neededForwards || defense.Count < neededDefense || goalies.Count < neededGoalies)
            throw new InvalidOperationException(
                $"Not enough synced players: need {neededForwards}F/{neededDefense}D/{neededGoalies}G, have {forwards.Count}F/{defense.Count}D/{goalies.Count}G.");

        Console.WriteLine(
            $"Drafting from the real top performers: {forwards[0].Name} ({forwards[0].FantasyPoints:0.#}pts) leads forwards, " +
            $"{defense[0].Name} ({defense[0].FantasyPoints:0.#}pts) leads defense, {goalies[0].Name} ({goalies[0].FantasyPoints:0.#}pts) leads goalies.");

        var rosters = Usernames.ToDictionary(u => u, _ => new List<long>());
        void SnakeDraft(List<RankedPlayer> pool, int rounds)
        {
            for (var round = 0; round < rounds && pool.Count > 0; round++)
            {
                var order = round % 2 == 0 ? Usernames : Usernames.Reverse().ToArray();
                foreach (var u in order)
                {
                    if (pool.Count == 0) break;
                    rosters[u].Add(pool[0].PlayerId);
                    pool.RemoveAt(0);
                }
            }
        }
        SnakeDraft(forwards, forwardSlots);
        SnakeDraft(defense, defenseSlots);
        SnakeDraft(goalies, goalieSlots);

        // Staggers each initial assignment's CreatedUtc (the activity-feed/
        // news-ticker timestamp) across the recent past, deterministically —
        // otherwise every one of these ~70 assignments shares the exact same
        // "now" moment and the ticker shows a wall of identical entries.
        // `From` (the real stat-counting start date) stays the true season
        // start regardless — this spread is display-only.
        var now = Timestamp.GetCurrentTimestamp();
        var assignmentIndex = 0;
        Timestamp StaggeredCreatedUtc()
        {
            var daysAgo = 1 + (assignmentIndex++ * 13) % 89;
            return Timestamp.FromDateTime(DateTime.UtcNow.AddDays(-daysAgo));
        }

        foreach (var (username, playerIds) in rosters)
        {
            await leagueDoc.Collection("teams").Document(username).SetAsync(new Team
            {
                Name = $"Team {char.ToUpper(username[0])}{username[1..]}",
                OwnerUsername = username,
                PlayerIds = playerIds,
                CreatedUtc = now,
            }, cancellationToken: ct);
            foreach (var pid in playerIds)
                await leagueDoc.Collection("assignments").AddAsync(new Assignment
                {
                    PlayerId = pid,
                    TeamUsername = username,
                    From = "2025-10-01",
                    CreationEvent = AssignmentCreationEvent.FreeAgent,
                    CreatedUtc = StaggeredCreatedUtc(),
                }, cancellationToken: ct);
        }
        var rosterSize = forwardSlots + defenseSlots + goalieSlots;
        Console.WriteLine($"Drafted {Usernames.Length} teams x {rosterSize} players ({forwardSlots}F/{defenseSlots}D/{goalieSlots}G).");
    }

    private async Task EstimateSalariesAsync(string season, CancellationToken ct)
    {
        // Same placeholder-salary approach as the standalone estimate-salaries
        // command (no free real salary source exists yet) — scales $3M-$14M
        // across the top 200 real scorers, flat $1M for everyone else, so
        // every drafted player (all top performers) ends up with a real,
        // differentiated salary rather than a flat default.
        const int topCount = 200;
        const long topMax = 14_000_000;
        const long topMin = 3_000_000;
        const long defaultSalary = 1_000_000;

        var seasonStatsSnap = await db.Collection("playerSeasonStats").WhereEqualTo("season", season).GetSnapshotAsync(ct);
        var ranked = seasonStatsSnap.Documents
            .Select(d => d.ConvertTo<PlayerSeasonStats>())
            .OrderByDescending(s => s.Goals + s.Assists)
            .ToList();

        var salaryByPlayerId = new Dictionary<long, long>();
        for (var i = 0; i < ranked.Count && i < topCount; i++)
        {
            var t = topCount > 1 ? (double)i / (topCount - 1) : 0;
            salaryByPlayerId[ranked[i].PlayerId] = (long)Math.Round(topMax - t * (topMax - topMin));
        }

        var allPlayers = await db.Collection("players").GetSnapshotAsync(ct);
        var now = Timestamp.GetCurrentTimestamp();
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
            }
            await batch.CommitAsync(ct);
        }
        Console.WriteLine($"Estimated salaries: {salaryByPlayerId.Count} top scorers scaled ${topMin:N0}-${topMax:N0}, everyone else ${defaultSalary:N0}.");
    }

    private static async Task<int> WipeCollectionAsync(CollectionReference col, CancellationToken ct)
    {
        var snapshot = await col.GetSnapshotAsync(ct);
        var count = 0;
        foreach (var chunk in snapshot.Documents.Chunk(500))
        {
            var batch = col.Database.StartBatch();
            foreach (var doc in chunk)
                batch.Delete(doc.Reference);
            await batch.CommitAsync(ct);
            count += chunk.Length;
        }
        return count;
    }
}
