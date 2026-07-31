using FantasyWarrior.Core.Stats;

namespace FantasyWarrior.Core.Scoring;

/// <summary>An inclusive "YYYY-MM-DD" date range.</summary>
public readonly record struct DateWindow(string From, string To);

/// <summary>
/// Date-window arithmetic over game lines. Every date here is the NHL's
/// Eastern game date (see <c>PoolClock</c>), stored as "YYYY-MM-DD", so plain
/// ordinal string comparison sorts chronologically — no parsing needed.
///
/// <see cref="Intersect"/> is the load-bearing function of the whole weekly
/// scoring model: it decides exactly which days of a scoring period a given
/// roster spot actually owns.
/// </summary>
public static class StatWindow
{
    /// <summary>Playoffs are excluded from pool scoring — regular season only.</summary>
    private const int RegularSeason = 2;

    public static bool Includes(PlayerGameStats line, string season, string from, string? to) =>
        line.Season == season
        && line.GameType == RegularSeason
        && string.CompareOrdinal(line.Date, from) >= 0
        && (to is null || string.CompareOrdinal(line.Date, to) <= 0);

    /// <summary>Totals for one player over an inclusive date range.</summary>
    public static StatLine Sum(IEnumerable<PlayerGameStats> lines, string season, string from, string? to) =>
        StatLine.Sum(lines.Where(l => Includes(l, season, from, to)).Select(StatLine.FromGameLine));

    /// <summary>
    /// The days of a scoring period that a roster spot actually owns, or null
    /// when it owns none.
    ///
    /// Three things narrow the window, and all three matter:
    /// <list type="bullet">
    /// <item>the spot may have opened after the period began, or closed before
    /// it ended — a player traded away mid-week keeps only the days he was
    /// actually on the roster;</item>
    /// <item><paramref name="lastStatDate"/> clamps the end, because scoring a
    /// day whose boxscores <c>stats-sync</c> has not written yet would bank a
    /// zero for it and never revisit it;</item>
    /// <item>if the spot opens after the last synced day, there is nothing to
    /// score yet — null, not an empty range.</item>
    /// </list>
    /// </summary>
    public static DateWindow? Intersect(
        string periodStart, string periodEnd,
        string spotStart, string? spotEnd,
        string lastStatDate)
    {
        var from = Max(periodStart, spotStart);
        var to = Min(periodEnd, spotEnd);
        to = Min(to, lastStatDate);
        return string.CompareOrdinal(from, to) <= 0 ? new DateWindow(from, to) : null;
    }

    private static string Max(string a, string b) => string.CompareOrdinal(a, b) >= 0 ? a : b;
    private static string Min(string a, string? b) => b is null ? a : string.CompareOrdinal(a, b) <= 0 ? a : b;
}
