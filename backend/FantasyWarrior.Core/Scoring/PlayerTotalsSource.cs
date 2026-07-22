using FantasyWarrior.Core.Stats;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Scoring;

/// <summary>
/// Builds league-agnostic raw season totals from `playerGameStats`.
/// One query per player (single-field index), season/gameType filtered
/// in memory — a player has at most ~100 lines per season.
/// </summary>
public static class PlayerTotalsSource
{
    private const int MaxParallel = 8;

    /// <summary>Regular-season totals for the given players.</summary>
    public static async Task<Dictionary<long, PlayerRawTotals>> FetchAsync(
        FirestoreDb db, IReadOnlyCollection<long> playerIds, string season, CancellationToken ct = default)
    {
        var results = new Dictionary<long, PlayerRawTotals>();
        foreach (var chunk in playerIds.Distinct().Chunk(MaxParallel))
        {
            var tasks = chunk.Select(async id =>
            {
                var lines = await FetchLinesAsync(db, id, ct);
                return (id, Totals: Aggregate(lines.Where(l => l.Season == season && l.GameType == 2)));
            });
            foreach (var (id, totals) in await Task.WhenAll(tasks))
                results[id] = totals;
        }
        return results;
    }

    public static async Task<IReadOnlyList<PlayerGameStats>> FetchLinesAsync(
        FirestoreDb db, long playerId, CancellationToken ct = default)
    {
        var snapshot = await db.Collection("playerGameStats")
            .WhereEqualTo("playerId", playerId)
            .GetSnapshotAsync(ct);
        return snapshot.Documents.Select(d => d.ConvertTo<PlayerGameStats>()).ToList();
    }

    public static PlayerRawTotals Aggregate(IEnumerable<PlayerGameStats> lines)
    {
        int gp = 0, goals = 0, assists = 0, wins = 0, otl = 0, so = 0;
        foreach (var line in lines)
        {
            gp++;
            goals += line.Goals ?? 0;
            assists += line.Assists ?? 0;
            if (line.Decision == "W") wins++;
            if (line.OtLoss == true) otl++;
            if (line.Shutout == true) so++;
        }
        return new PlayerRawTotals(gp, goals, assists, wins, otl, so);
    }
}
