using FantasyWarrior.Core.Stats;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Nhl;

/// <summary>
/// Syncs finished NHL games and their per-player boxscore lines into
/// Firestore (`games`, `playerGameStats`). Idempotent upserts — safe to
/// re-run on any date range (backfill or catch-up after a missed cron).
/// </summary>
public sealed class StatsSyncJob(NhlApiClient nhl, FirestoreDb db)
{
    private const int FirestoreBatchLimit = 500;
    private static readonly string[] FinishedStates = ["OFF", "FINAL"];

    public async Task<(int Games, int PlayerLines)> RunAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var totalGames = 0;
        var totalLines = 0;
        foreach (var date in EnumerateDates(from, to))
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var games = (await nhl.GetDailyScoresAsync(dateStr, ct))
                .Where(g => FinishedStates.Contains(g.GameState) && g.GameDate == dateStr)
                .ToList();
            var writes = new List<(DocumentReference Doc, object Data)>();
            var now = Timestamp.GetCurrentTimestamp();

            foreach (var game in games)
            {
                var box = await nhl.GetBoxscoreAsync(game.Id, ct);
                if (box is null)
                {
                    Console.WriteLine($"  ! no boxscore for game {game.Id}, skipped");
                    continue;
                }

                writes.Add((db.Collection("games").Document(game.Id.ToString()), ToGame(game, now)));
                foreach (var line in BuildPlayerLines(game, box, now))
                {
                    writes.Add((db.Collection("playerGameStats").Document($"{game.Id}_{line.PlayerId}"), line));
                    totalLines++;
                }
                totalGames++;
            }

            foreach (var chunk in writes.Chunk(FirestoreBatchLimit))
            {
                var batch = db.StartBatch();
                foreach (var (doc, data) in chunk)
                    batch.Set(doc, data);
                await batch.CommitAsync(ct);
            }

            Console.WriteLine($"  {dateStr}: {games.Count} finished games, {writes.Count} docs");
        }

        Console.WriteLine($"StatsSync: {totalGames} games, {totalLines} player lines upserted");
        return (totalGames, totalLines);
    }

    public static IEnumerable<DateOnly> EnumerateDates(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
            yield return d;
    }

    private static Game ToGame(ScoreGameDto game, Timestamp now) => new()
    {
        NhlGameId = game.Id,
        Season = game.Season.ToString(),
        GameType = game.GameType,
        Date = game.GameDate,
        HomeAbbrev = game.HomeTeam.Abbrev,
        AwayAbbrev = game.AwayTeam.Abbrev,
        HomeScore = game.HomeTeam.Score,
        AwayScore = game.AwayTeam.Score,
        LastPeriodType = game.GameOutcome?.LastPeriodType ?? "",
        SyncedUtc = now,
    };

    private static IEnumerable<PlayerGameStats> BuildPlayerLines(ScoreGameDto game, BoxscoreDto box, Timestamp now)
    {
        var teams = new[]
        {
            (Players: box.PlayerByGameStats.AwayTeam, Team: game.AwayTeam.Abbrev, Opponent: game.HomeTeam.Abbrev),
            (Players: box.PlayerByGameStats.HomeTeam, Team: game.HomeTeam.Abbrev, Opponent: game.AwayTeam.Abbrev),
        };

        foreach (var (players, team, opponent) in teams)
        {
            foreach (var skater in players.Forwards.Concat(players.Defense))
                yield return ToSkaterLine(skater, game, team, opponent, now);

            foreach (var goalie in players.Goalies.Where(g => TimeOnIce(g.Toi) > 0))
                yield return ToGoalieLine(goalie, players.Goalies, game, team, opponent, now);
        }
    }

    private static PlayerGameStats ToSkaterLine(
        BoxPlayerDto p, ScoreGameDto game, string team, string opponent, Timestamp now) =>
        new()
        {
            GameId = game.Id,
            Date = game.GameDate,
            Season = game.Season.ToString(),
            GameType = game.GameType,
            PlayerId = p.PlayerId,
            Name = p.Name?.Default ?? "",
            TeamAbbrev = team,
            OpponentAbbrev = opponent,
            Position = p.Position,
            IsGoalie = false,
            Toi = p.Toi,
            Pim = p.Pim,
            Goals = p.Goals ?? 0,
            Assists = p.Assists ?? 0,
            Points = p.Points ?? 0,
            PlusMinus = p.PlusMinus ?? 0,
            Shots = p.Sog ?? 0,
            Hits = p.Hits ?? 0,
            BlockedShots = p.BlockedShots ?? 0,
            PowerPlayGoals = p.PowerPlayGoals ?? 0,
            SyncedUtc = now,
        };

    private static PlayerGameStats ToGoalieLine(
        BoxPlayerDto goalie, List<BoxPlayerDto> teamGoalies, ScoreGameDto game,
        string team, string opponent, Timestamp now) =>
        new()
        {
            GameId = game.Id,
            Date = game.GameDate,
            Season = game.Season.ToString(),
            GameType = game.GameType,
            PlayerId = goalie.PlayerId,
            Name = goalie.Name?.Default ?? "",
            TeamAbbrev = team,
            OpponentAbbrev = opponent,
            Position = goalie.Position,
            IsGoalie = true,
            Toi = goalie.Toi,
            Pim = goalie.Pim,
            ShotsAgainst = goalie.ShotsAgainst ?? 0,
            Saves = goalie.Saves ?? 0,
            GoalsAgainst = goalie.GoalsAgainst ?? 0,
            Decision = goalie.Decision,
            Starter = goalie.Starter,
            Shutout = IsShutout(goalie, teamGoalies),
            OtLoss = goalie.Decision == "O",
            SyncedUtc = now,
        };

    /// <summary>
    /// NHL rule approximation: a shutout requires being the only goalie who
    /// played for the team and allowing zero goals (shootout goals excluded —
    /// they are not in goalsAgainst).
    /// </summary>
    public static bool IsShutout(BoxPlayerDto goalie, IEnumerable<BoxPlayerDto> teamGoalies) =>
        (goalie.GoalsAgainst ?? 0) == 0
        && TimeOnIce(goalie.Toi) > 0
        && teamGoalies.Count(g => TimeOnIce(g.Toi) > 0) == 1;

    /// <summary>Parses "MM:SS" (minutes can exceed 59 in OT) to seconds; 0 when empty/invalid.</summary>
    public static int TimeOnIce(string? toi)
    {
        if (string.IsNullOrWhiteSpace(toi))
            return 0;
        var parts = toi.Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var s)
            ? m * 60 + s
            : 0;
    }
}
