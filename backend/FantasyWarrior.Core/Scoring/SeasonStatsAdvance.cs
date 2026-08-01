using FantasyWarrior.Core.Stats;

namespace FantasyWarrior.Core.Scoring;

/// <summary>What to do with one player's cached season totals when the cursor moves.</summary>
public enum SeasonStatsAction
{
    /// <summary>Already includes the target day — leave it alone.</summary>
    Skip,

    /// <summary>Add the new days' lines to what is already stored.</summary>
    Add,

    /// <summary>Stored totals can't be extended — rebuild from the player's own lines.</summary>
    Rebuild,
}

/// <summary>
/// The rule for advancing a consolidated season aggregate to a new date.
///
/// Pure, because getting it wrong is silent: adding a day twice inflates a
/// player's season forever, and rebuilding when adding would do costs a full
/// rescan per player per day.
/// </summary>
public static class SeasonStatsAdvance
{
    /// <summary>
    /// <paramref name="storedThroughDate"/> is what the cached document claims
    /// to cover; <paramref name="targetDate"/> is where the cursor is moving to.
    ///
    /// Adding is only safe when the stored cursor sits strictly before the
    /// target and the new lines start the day after it — any other shape (no
    /// cursor at all, a cursor already past the target, or a gap) has to be
    /// rebuilt, because the stored number cannot be reconciled with the range
    /// being added.
    /// </summary>
    public static SeasonStatsAction Decide(string? storedThroughDate, string targetDate)
    {
        if (storedThroughDate is null) return SeasonStatsAction.Rebuild;

        var comparison = string.CompareOrdinal(storedThroughDate, targetDate);
        if (comparison >= 0) return SeasonStatsAction.Skip;
        return SeasonStatsAction.Add;
    }

    /// <summary>
    /// The date range whose lines still need to be added, or null when there is
    /// nothing to add. The lower bound is the day *after* what is already
    /// counted — including it again is exactly the double-count this guards.
    /// </summary>
    public static (string From, string To)? RangeToAdd(string? storedThroughDate, string targetDate)
    {
        if (Decide(storedThroughDate, targetDate) != SeasonStatsAction.Add) return null;
        var from = DateOnly.Parse(storedThroughDate!).AddDays(1);
        return (from.ToString("yyyy-MM-dd"), targetDate);
    }

    /// <summary>Adds a range's totals to an existing aggregate.</summary>
    public static PlayerRawTotals Plus(PlayerRawTotals stored, PlayerRawTotals added) => new(
        GamesPlayed: stored.GamesPlayed + added.GamesPlayed,
        Goals: stored.Goals + added.Goals,
        Assists: stored.Assists + added.Assists,
        Wins: stored.Wins + added.Wins,
        OtLosses: stored.OtLosses + added.OtLosses,
        Shutouts: stored.Shutouts + added.Shutouts,
        PlusMinus: stored.PlusMinus + added.PlusMinus,
        Pim: stored.Pim + added.Pim,
        Shots: stored.Shots + added.Shots,
        Hits: stored.Hits + added.Hits,
        BlockedShots: stored.BlockedShots + added.BlockedShots,
        GoalsAgainst: stored.GoalsAgainst + added.GoalsAgainst,
        Saves: stored.Saves + added.Saves,
        ShotsAgainst: stored.ShotsAgainst + added.ShotsAgainst);

    /// <summary>Maps a cached document back to raw totals.</summary>
    public static PlayerRawTotals ToTotals(PlayerSeasonStats s) => new(
        s.GamesPlayed, s.Goals, s.Assists, s.Wins, s.OtLosses, s.Shutouts,
        s.PlusMinus, s.Pim, s.Shots, s.Hits, s.BlockedShots, s.GoalsAgainst, s.Saves, s.ShotsAgainst);
}
