using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>
/// Lookups every endpoint needs, in one place so they resolve identically.
/// </summary>
public static class Queries
{
    /// <summary>Trimmed and lowercased — the form every username is stored in.</summary>
    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    /// <summary>
    /// A league by its public code. The frontend calls this the league "id" and
    /// keeps it in localStorage; internally it is <see cref="League.JoinCode"/>,
    /// which is also what a GM types to join. One string, two jobs, exactly as
    /// the Firestore document id used to be.
    /// </summary>
    public static Task<League?> LeagueByCodeAsync(
        FantasyWarriorDbContext db, string code, CancellationToken ct = default) =>
        db.Leagues.FirstOrDefaultAsync(l => l.JoinCode == code, ct);

    public static Task<Team?> TeamAsync(
        FantasyWarriorDbContext db, int leagueId, string username, CancellationToken ct = default) =>
        db.Teams.FirstOrDefaultAsync(t => t.LeagueId == leagueId && t.Owner!.Username == username, ct);

    /// <summary>A league's scale as the engine consumes it: stat name to value.</summary>
    public static async Task<Dictionary<string, double>> ScaleAsync(
        FantasyWarriorDbContext db, int leagueId, CancellationToken ct = default) =>
        await db.LeagueScoringRules
            .Where(r => r.LeagueId == leagueId)
            .ToDictionaryAsync(r => r.StatKey, r => r.PointValue, ct);

    /// <summary>
    /// The week being played, or the one asked for.
    ///
    /// "Current" is the last week whose start has passed, on the NHL's Eastern
    /// game date — never raw UTC, which disagrees for five hours every night and
    /// would show the wrong week to anyone loading the app late in the evening.
    /// Before the season it is week 1.
    /// </summary>
    public static async Task<(Period? Chosen, List<Period> All)> ResolvePeriodAsync(
        FantasyWarriorDbContext db, string season, int? number, DateOnly today,
        CancellationToken ct = default)
    {
        var all = await db.Periods
            .Where(p => p.Season == season)
            .OrderBy(p => p.Number)
            .ToListAsync(ct);
        if (all.Count == 0) return (null, all);

        var chosen = number is not null
            ? all.FirstOrDefault(p => p.Number == number)
            : all.LastOrDefault(p => p.StartDate <= today) ?? all[0];
        return (chosen, all);
    }

    /// <summary>
    /// NHL points (goals + assists) per player, keyed by id as a string — the
    /// shape the frontend uses to rank players in trades and the news ticker.
    /// </summary>
    public static async Task<Dictionary<string, int>> NhlPointsAsync(
        FantasyWarriorDbContext db, string season, IReadOnlyCollection<long> playerIds,
        DateOnly? asOf, CancellationToken ct = default)
    {
        var totals = await SeasonTotalsAsync(db, season, playerIds, asOf, ct);
        return totals.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Goals + kv.Value.Assists);
    }

    /// <summary>
    /// Season totals for a set of players, bounded to a simulated day when one
    /// is running.
    ///
    /// The bound is not optional. Without it the Stats screen would report
    /// end-of-season numbers while the player card — which has always respected
    /// the cursor — reports the same player at zero, and the two would disagree
    /// on the same screen. It is also just wrong: at the eve of a replay, nobody
    /// has played a game.
    ///
    /// <c>vPlayerSeasonStats</c> serves the unbounded case because a view cannot
    /// take an argument; the bounded case is the same aggregation with a WHERE.
    /// </summary>
    public static async Task<Dictionary<long, SeasonTotals>> SeasonTotalsAsync(
        FantasyWarriorDbContext db, string season, IReadOnlyCollection<long> playerIds,
        DateOnly? asOf, CancellationToken ct = default)
    {
        if (playerIds.Count == 0) return [];

        if (asOf is null)
            return await db.PlayerSeasonStats
                .Where(s => s.Season == season && playerIds.Contains(s.PlayerId))
                .Select(s => new SeasonTotals(
                    s.PlayerId, s.GamesPlayed, s.Goals, s.Assists, s.PlusMinus, s.Pim, s.Shots,
                    s.Hits, s.BlockedShots, s.Wins, s.OtLosses, s.Shutouts, s.GoalsAgainst,
                    s.Saves, s.ShotsAgainst))
                .ToDictionaryAsync(s => s.PlayerId, ct);

        return await db.PlayerGameStats
            .Where(l => l.Season == season
                        && l.GameType == Data.Entities.GameType.RegularSeason
                        && l.GameDate <= asOf
                        && playerIds.Contains(l.PlayerId))
            .GroupBy(l => l.PlayerId)
            .Select(g => new SeasonTotals(
                g.Key,
                g.Count(),
                g.Sum(l => l.Goals ?? 0),
                g.Sum(l => l.Assists ?? 0),
                g.Sum(l => l.PlusMinus ?? 0),
                g.Sum(l => l.Pim),
                g.Sum(l => l.Shots ?? 0),
                g.Sum(l => l.Hits ?? 0),
                g.Sum(l => l.BlockedShots ?? 0),
                g.Count(l => l.Decision == "W"),
                g.Count(l => l.OtLoss == true),
                g.Count(l => l.Shutout == true),
                g.Sum(l => l.GoalsAgainst ?? 0),
                g.Sum(l => l.Saves ?? 0),
                g.Sum(l => l.ShotsAgainst ?? 0)))
            .ToDictionaryAsync(s => s.PlayerId, ct);
    }

    /// <summary>Contract cap hits for the league's season; absent means unknown.</summary>
    public static async Task<Dictionary<long, long>> CapHitsAsync(
        FantasyWarriorDbContext db, string season, IReadOnlyCollection<long> playerIds,
        CancellationToken ct = default)
    {
        if (playerIds.Count == 0) return [];
        return await db.PlayerContracts
            .Where(c => c.Season == season && playerIds.Contains(c.PlayerId))
            .ToDictionaryAsync(c => c.PlayerId, c => c.CapHit, ct);
    }

    /// <summary>
    /// Who, among these players, is unavailable right now — one entry per
    /// player, absent when he is fit.
    ///
    /// Two sources can both report the same man, so this takes the one
    /// reported first: it is the one whose "hurt since" date is true, and a
    /// screen showing "Knee" where the other site says "Lower Body" is not a
    /// discrepancy worth a second row on a grid line.
    /// </summary>
    public static async Task<Dictionary<long, PlayerInjuryStatus>> InjuriesAsync(
        FantasyWarriorDbContext db, IReadOnlyCollection<long> playerIds, CancellationToken ct = default)
    {
        if (playerIds.Count == 0) return [];
        var rows = await db.PlayerInjuries
            .AsNoTracking()
            .Where(i => i.ResolvedUtc == null && playerIds.Contains(i.PlayerId))
            .OrderBy(i => i.ReportedUtc)
            .Select(i => new PlayerInjuryStatus(i.PlayerId, i.Status, i.InjuryType, i.ReportedUtc, i.Source))
            .ToListAsync(ct);
        return rows
            .GroupBy(i => i.PlayerId)
            .ToDictionary(g => g.Key, g => g.First());
    }
}

/// <summary>A player's current unavailability, flattened for the API.</summary>
public sealed record PlayerInjuryStatus(
    long PlayerId, string Status, string? InjuryType, DateTime ReportedUtc, string Source);

/// <summary>
/// One player's regular-season counting stats. A record rather than the view
/// type so the bounded and unbounded paths return the same shape.
/// </summary>
public sealed record SeasonTotals(
    long PlayerId, int GamesPlayed, int Goals, int Assists, int PlusMinus, int Pim,
    int Shots, int Hits, int BlockedShots, int Wins, int OtLosses, int Shutouts,
    int GoalsAgainst, int Saves, int ShotsAgainst);
