using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Stats;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Simulation;

/// <summary>
/// Moves every player's consolidated season aggregate forward to a given day.
///
/// This is what makes a replayed season look like a real one from the outside:
/// on the simulated 23rd of November a player card shows his totals through
/// the 23rd, not his end-of-April line.
///
/// It is incremental by design. One date-range query returns every player's
/// lines for the days being added, and each affected player's stored totals are
/// summed with them — instead of the old behaviour, which re-scanned a player's
/// entire career on every cache miss. Advancing a week costs one query of a few
/// thousand documents rather than hundreds of per-player scans.
/// </summary>
public sealed class AdvanceSeasonStatsJob(FirestoreDb db)
{
    private const int BatchLimit = 450;

    public async Task<(int Players, int Reads, int Writes)> RunAsync(
        string season, string targetDate, bool dryRun, CancellationToken ct = default)
    {
        // Everyone currently cached for this season; anyone absent gets built
        // from scratch the first time they are scored.
        var cachedSnap = await db.Collection("playerSeasonStats")
            .WhereEqualTo("season", season).GetSnapshotAsync(ct);
        var cached = cachedSnap.Documents.ToDictionary(
            d => d.ConvertTo<PlayerSeasonStats>().PlayerId,
            d => d.ConvertTo<PlayerSeasonStats>());
        var reads = Math.Max(cachedSnap.Count, 1);

        // The widest range any player still needs. Pulling it once and slicing
        // in memory is what keeps this to a single query.
        var earliest = cached.Values
            .Select(c => SeasonStatsAdvance.RangeToAdd(c.ThroughDate, targetDate)?.From)
            .Where(f => f is not null)
            .DefaultIfEmpty(null)
            .Min();

        var rebuilds = cached.Values.Count(c => SeasonStatsAdvance.Decide(c.ThroughDate, targetDate) == SeasonStatsAction.Rebuild);
        if (earliest is null && rebuilds == 0)
        {
            Console.WriteLine($"  Season stats already through {targetDate} — nothing to do.");
            return (0, reads, 0);
        }

        // An add only needs the gap since the stored cursor; a rebuild needs the
        // season from the beginning, so one of those present widens the window
        // for everyone. Cheaper than issuing two queries.
        var from = rebuilds > 0 ? SeasonStart(season) : earliest!;
        var linesSnap = await db.Collection("playerGameStats")
            .WhereGreaterThanOrEqualTo("date", from)
            .WhereLessThanOrEqualTo("date", targetDate)
            .GetSnapshotAsync(ct);
        reads += Math.Max(linesSnap.Count, 1);

        var lines = linesSnap.Documents
            .Select(d => d.ConvertTo<PlayerGameStats>())
            .Where(l => l.Season == season && l.GameType == 2)
            .ToLookup(l => l.PlayerId);

        Console.WriteLine($"  Fetched {linesSnap.Count} game lines up to {targetDate} "
            + $"({lines.Sum(g => g.Count())} in season/regular), {cached.Count} cached player(s).");

        var batch = db.StartBatch();
        var queued = 0;
        var writes = 0;
        var touched = 0;

        // Players with lines in the window but no cache entry yet also need one.
        var playerIds = cached.Keys.Union(lines.Select(g => g.Key)).ToList();

        foreach (var playerId in playerIds)
        {
            var stored = cached.GetValueOrDefault(playerId);
            var action = stored is null
                ? SeasonStatsAction.Rebuild
                : SeasonStatsAdvance.Decide(stored.ThroughDate, targetDate);
            if (action == SeasonStatsAction.Skip) continue;

            PlayerRawTotals totals;
            if (action == SeasonStatsAction.Add)
            {
                var range = SeasonStatsAdvance.RangeToAdd(stored!.ThroughDate, targetDate)!.Value;
                var added = PlayerTotalsSource.Aggregate(lines[playerId].Where(l =>
                    string.CompareOrdinal(l.Date, range.From) >= 0
                    && string.CompareOrdinal(l.Date, range.To) <= 0));
                totals = SeasonStatsAdvance.Plus(SeasonStatsAdvance.ToTotals(stored), added);
            }
            else
            {
                totals = PlayerTotalsSource.Aggregate(lines[playerId]);
            }

            touched++;
            if (dryRun) continue;

            batch.Set(
                db.Collection("playerSeasonStats").Document(PlayerTotalsSource.SeasonStatsDocId(season, playerId)),
                ToDocument(season, playerId, totals, targetDate));
            writes++;
            if (++queued < BatchLimit) continue;

            await batch.CommitAsync(ct);
            batch = db.StartBatch();
            queued = 0;
        }
        if (queued > 0 && !dryRun) await batch.CommitAsync(ct);

        Console.WriteLine($"  {touched} player aggregate(s) advanced to {targetDate}"
            + (rebuilds > 0 ? $" ({rebuilds} rebuilt from scratch)" : "")
            + (dryRun ? " [dry run]" : "") + ".");
        return (touched, reads, writes);
    }

    /// <summary>
    /// A lower bound no game of the season can precede. An NHL season labelled
    /// "20252026" cannot start before July 2025, so this is safely early without
    /// widening the query to every season ever stored.
    /// </summary>
    private static string SeasonStart(string season) => $"{season[..4]}-07-01";

    private static PlayerSeasonStats ToDocument(string season, long playerId, PlayerRawTotals t, string throughDate) => new()
    {
        Season = season,
        PlayerId = playerId,
        GamesPlayed = t.GamesPlayed,
        Goals = t.Goals,
        Assists = t.Assists,
        PlusMinus = t.PlusMinus,
        Pim = t.Pim,
        Shots = t.Shots,
        Hits = t.Hits,
        BlockedShots = t.BlockedShots,
        Wins = t.Wins,
        OtLosses = t.OtLosses,
        Shutouts = t.Shutouts,
        GoalsAgainst = t.GoalsAgainst,
        Saves = t.Saves,
        ShotsAgainst = t.ShotsAgainst,
        ThroughDate = throughDate,
        UpdatedUtc = Timestamp.GetCurrentTimestamp(),
    };
}
