using FantasyWarrior.Core.Time;

namespace FantasyWarrior.Core.Periods;

/// <summary>A generated week, before it becomes a row.</summary>
public readonly record struct PeriodSpan(int Index, DateOnly Start, DateOnly End)
{
    public string StartIso => PoolClock.Iso(Start);
    public string EndIso => PoolClock.Iso(End);
}

/// <summary>
/// Generates and locates the weekly scoring calendar. Pure — no I/O, no clock.
///
/// Weeks run Monday through Sunday on the NHL's Eastern game date. That date is
/// what <c>playerGameStats.date</c> already holds, so a West Coast game starting
/// Sunday 22:30 ET and ending Monday 01:15 ET carries the Sunday date and
/// belongs to the Sunday's week. Comparing wall-clock timestamps instead would
/// misfile exactly those games.
/// </summary>
public static class PeriodCalendar
{
    /// <summary>
    /// The Monday on or before a date — the anchor every week index counts from.
    /// </summary>
    public static DateOnly MondayOnOrBefore(DateOnly date) =>
        date.AddDays(-((int)date.DayOfWeek + 6) % 7);

    /// <summary>
    /// Every week covering a season, week 1 starting on the Monday on or before
    /// the first game. The first and last weeks are usually partial (a season
    /// rarely starts on a Monday or ends on a Sunday); that is fine, a short
    /// week is just a week with fewer games.
    /// </summary>
    public static IReadOnlyList<PeriodSpan> Generate(DateOnly firstGameDate, DateOnly lastGameDate)
    {
        if (lastGameDate < firstGameDate) return [];

        var spans = new List<PeriodSpan>();
        var start = MondayOnOrBefore(firstGameDate);
        var index = 1;
        while (start <= lastGameDate)
        {
            spans.Add(new PeriodSpan(index, start, start.AddDays(6)));
            start = start.AddDays(7);
            index++;
        }
        return spans;
    }

    /// <summary>
    /// The 1-based week number a date falls in, counting from the season anchor.
    /// Returns null before the anchor. Callers that need "is it inside the
    /// season" should check against the generated list — this deliberately does
    /// not cap at the season end so a stray late game still resolves.
    /// </summary>
    public static int? IndexFor(DateOnly anchorMonday, DateOnly date)
    {
        if (date < anchorMonday) return null;
        return (date.DayNumber - anchorMonday.DayNumber) / 7 + 1;
    }

    /// <summary>
    /// Midnight ET on the given day, as UTC — the default lineup lock for a week.
    /// </summary>
    public static DateTime LockUtcFor(DateOnly startDate) =>
        TimeZoneInfo.ConvertTimeToUtc(startDate.ToDateTime(TimeOnly.MinValue), PoolClock.EtZone);
}
