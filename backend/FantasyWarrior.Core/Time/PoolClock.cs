namespace FantasyWarrior.Core.Time;

/// <summary>
/// The single definition of "what day is it" for the whole pool.
///
/// Everything in this system is anchored to the NHL's Eastern-Time game day,
/// because <c>playerGameStats.date</c> is the NHL API's <c>gameDate</c> written
/// verbatim (see <c>StatsSyncJob</c>). A West Coast game that starts Sunday
/// 22:30 ET and ends Monday 01:15 ET carries <c>date = Sunday</c> and therefore
/// belongs to Sunday's scoring period. Comparing a UTC timestamp against a
/// period boundary is the obvious-but-wrong implementation: it would shift a
/// whole week for anyone on the wrong side of midnight UTC.
///
/// Before this existed, three copies of <c>EtToday()</c> and two ad hoc
/// "UTC minus one day" expressions disagreed with each other. They only
/// coincided because the nightly cron happens to run at 09:30 UTC.
///
/// <paramref name="utcNow"/> is always a parameter, never <c>DateTime.UtcNow</c>
/// read inside — that is what makes these functions unit-testable across DST.
/// </summary>
public static class PoolClock
{
    /// <summary>
    /// Eastern Time, resolved by IANA id first (Linux/Cloud Run/GitHub Actions)
    /// then by Windows id (Nick's dev box). .NET 10 usually accepts both, but
    /// not on every host configuration, so the fallback stays.
    /// </summary>
    public static TimeZoneInfo EtZone { get; } = ResolveEtZone();

    private static TimeZoneInfo ResolveEtZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    }

    /// <summary>The current NHL game day.</summary>
    public static DateOnly TodayEt(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, EtZone).DateTime);

    /// <summary>
    /// The most recent game day whose boxscores are fully synced — i.e. ET
    /// yesterday. This is the upper bound of every scoring window: scoring a
    /// day that <c>stats-sync</c> has not written yet would bank a zero and
    /// then never revisit it.
    /// </summary>
    public static DateOnly LastStatDate(DateTimeOffset utcNow) => TodayEt(utcNow).AddDays(-1);

    /// <summary>The <c>yyyy-MM-dd</c> form used as a Firestore field value everywhere.</summary>
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    /// <inheritdoc cref="TodayEt(DateTimeOffset)"/>
    public static string TodayEtIso(DateTimeOffset utcNow) => Iso(TodayEt(utcNow));

    /// <inheritdoc cref="LastStatDate(DateTimeOffset)"/>
    public static string LastStatDateIso(DateTimeOffset utcNow) => Iso(LastStatDate(utcNow));
}
