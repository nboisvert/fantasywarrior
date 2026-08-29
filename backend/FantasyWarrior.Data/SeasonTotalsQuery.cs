using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data;

/// <summary>
/// Season totals for a set of players, bounded to a simulated day when one is
/// running.
///
/// <b>Lives here rather than in the API</b> because the off-season protection
/// slate needs it too, and that runs from a job as well as from an endpoint
/// (see <c>Rosters/ProtectionSlate.cs</c>). A second copy of this aggregation
/// would be a second answer to "what did he produce this season".
///
/// The bound is not optional. Without it the Stats screen would report
/// end-of-season numbers while the player card — which has always respected the
/// cursor — reports the same player at zero, and the two would disagree on the
/// same screen. It is also just wrong: at the eve of a replay, nobody has played
/// a game.
///
/// <c>vPlayerSeasonStats</c> serves the unbounded case because a view cannot
/// take an argument; the bounded case is the same aggregation with a WHERE.
/// </summary>
public static class SeasonTotalsQuery
{
    public static async Task<Dictionary<long, SeasonTotals>> ForAsync(
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
                        && l.GameType == Entities.GameType.RegularSeason
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
}
