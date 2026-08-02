namespace FantasyWarrior.Core.Scoring;

/// <summary>An inclusive range of NHL game days.</summary>
public readonly record struct DateWindow(DateOnly From, DateOnly To)
{
    public string FromIso => From.ToString("yyyy-MM-dd");
    public string ToIso => To.ToString("yyyy-MM-dd");
}

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
        DateOnly periodStart, DateOnly periodEnd,
        DateOnly spotStart, DateOnly? spotEnd,
        DateOnly lastStatDate)
    {
        var from = spotStart >= periodStart ? spotStart : periodStart;
        var to = spotEnd is not null && spotEnd < periodEnd ? spotEnd.Value : periodEnd;
        if (lastStatDate < to) to = lastStatDate;
        return from <= to ? new DateWindow(from, to) : null;
    }
}
