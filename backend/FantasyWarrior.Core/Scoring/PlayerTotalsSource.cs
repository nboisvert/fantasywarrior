using FantasyWarrior.Core.Stats;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Scoring;

/// <summary>
/// Builds league-agnostic raw season totals from `playerGameStats`, with a
/// write-through cache in `playerSeasonStats` (one doc per season/player).
///
/// <see cref="FetchAsync"/> (the nightly scoring path) always recomputes
/// from the per-game lines — freshness matters more than the read cost at
/// this app's scale — and then writes the cache so other consumers (the
/// player-card API, a future season-leaders view, a redraft) can read a
/// single document instead of re-scanning that player's whole season.
/// </summary>
public static class PlayerTotalsSource
{
    private const int MaxParallel = 8;

    /// <summary>Regular-season totals for the given players. Always fresh; refreshes the cache as a side effect.</summary>
    public static async Task<Dictionary<long, PlayerRawTotals>> FetchAsync(
        FirestoreDb db, IReadOnlyCollection<long> playerIds, string season, CancellationToken ct = default)
    {
        var results = new Dictionary<long, PlayerRawTotals>();
        foreach (var chunk in playerIds.Distinct().Chunk(MaxParallel))
        {
            var tasks = chunk.Select(async id =>
            {
                var lines = await FetchLinesAsync(db, id, ct);
                var totals = Aggregate(lines.Where(l => l.Season == season && l.GameType == 2));
                await CacheAsync(db, season, id, totals, ct);
                return (id, Totals: totals);
            });
            foreach (var (id, totals) in await Task.WhenAll(tasks))
                results[id] = totals;
        }
        return results;
    }

    /// <summary>
    /// Cache-first totals — for on-demand views (e.g. a roster stats page)
    /// where nightly-fresh data is fine. Falls back to a live per-game
    /// aggregation (and populates the cache) for any player not yet cached.
    /// </summary>
    public static async Task<Dictionary<long, PlayerRawTotals>> FetchWithCacheAsync(
        FirestoreDb db, IReadOnlyCollection<long> playerIds, string season, CancellationToken ct = default)
    {
        var results = new Dictionary<long, PlayerRawTotals>();
        var misses = new List<long>();
        foreach (var id in playerIds.Distinct())
        {
            var cached = await TryGetCachedAsync(db, season, id, ct);
            if (cached is not null)
                results[id] = cached;
            else
                misses.Add(id);
        }
        if (misses.Count > 0)
            foreach (var (id, totals) in await FetchAsync(db, misses, season, ct))
                results[id] = totals;
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
        int gp = 0, goals = 0, assists = 0, wins = 0, otl = 0, so = 0,
            plusMinus = 0, pim = 0, shots = 0, hits = 0, blocked = 0, ga = 0, saves = 0, sa = 0;
        foreach (var line in lines)
        {
            gp++;
            goals += line.Goals ?? 0;
            assists += line.Assists ?? 0;
            plusMinus += line.PlusMinus ?? 0;
            pim += line.Pim;
            shots += line.Shots ?? 0;
            hits += line.Hits ?? 0;
            blocked += line.BlockedShots ?? 0;
            if (line.Decision == "W") wins++;
            if (line.OtLoss == true) otl++;
            if (line.Shutout == true) so++;
            ga += line.GoalsAgainst ?? 0;
            saves += line.Saves ?? 0;
            sa += line.ShotsAgainst ?? 0;
        }
        return new PlayerRawTotals(gp, goals, assists, wins, otl, so, plusMinus, pim, shots, hits, blocked, ga, saves, sa);
    }

    public static string SeasonStatsDocId(string season, long playerId) => $"{season}_{playerId}";

    /// <summary>Reads the consolidated cache doc, if present. Does not fall back to computing.</summary>
    public static async Task<PlayerRawTotals?> TryGetCachedAsync(
        FirestoreDb db, string season, long playerId, CancellationToken ct = default)
    {
        var snap = await db.Collection("playerSeasonStats")
            .Document(SeasonStatsDocId(season, playerId))
            .GetSnapshotAsync(ct);
        if (!snap.Exists)
            return null;
        var s = snap.ConvertTo<PlayerSeasonStats>();
        return new PlayerRawTotals(
            s.GamesPlayed, s.Goals, s.Assists, s.Wins, s.OtLosses, s.Shutouts,
            s.PlusMinus, s.Pim, s.Shots, s.Hits, s.BlockedShots, s.GoalsAgainst, s.Saves, s.ShotsAgainst);
    }

    public static async Task CacheAsync(
        FirestoreDb db, string season, long playerId, PlayerRawTotals totals, CancellationToken ct = default)
    {
        await db.Collection("playerSeasonStats").Document(SeasonStatsDocId(season, playerId)).SetAsync(new PlayerSeasonStats
        {
            Season = season,
            PlayerId = playerId,
            GamesPlayed = totals.GamesPlayed,
            Goals = totals.Goals,
            Assists = totals.Assists,
            PlusMinus = totals.PlusMinus,
            Pim = totals.Pim,
            Shots = totals.Shots,
            Hits = totals.Hits,
            BlockedShots = totals.BlockedShots,
            Wins = totals.Wins,
            OtLosses = totals.OtLosses,
            Shutouts = totals.Shutouts,
            GoalsAgainst = totals.GoalsAgainst,
            Saves = totals.Saves,
            ShotsAgainst = totals.ShotsAgainst,
            UpdatedUtc = Timestamp.GetCurrentTimestamp(),
        }, cancellationToken: ct);
    }
}
