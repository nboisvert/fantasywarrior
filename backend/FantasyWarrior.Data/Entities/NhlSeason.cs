namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One NHL season and the dates it spans — the system-wide calendar every
/// league runs on.
///
/// <b>The season string stays the key.</b> <c>"20262027"</c> is the NHL's own
/// identifier and is already the join value on <see cref="Game"/>,
/// <see cref="PlayerGameStat"/>, <see cref="Period"/>,
/// <see cref="PlayerContract"/>, <see cref="League"/> and
/// <see cref="LeagueSeason"/>; conversion stays a pure function in
/// <see cref="Core.Seasons.Season"/>. What this table adds is the one thing the
/// string cannot carry: <b>dates</b>. Nothing gains a foreign key to it — a
/// constraint on 51k stat rows would guarantee only what the string already
/// does, and would force an insert order on every sync job.
///
/// <b>Declared, not observed.</b> These are the schedule the NHL published,
/// which is why they can exist before a single <see cref="Game"/> is imported —
/// the reason this table exists at all. <c>period-init</c> derived its bounds
/// from <c>MIN/MAX(Games.GameDate)</c> and could therefore not build next
/// season's calendar until the schedule had been synced. Where both are known
/// they are reconciled by <see cref="Core.Seasons.SeasonBounds"/>, never by one
/// silently winning.
///
/// There is deliberately no "is it the current season" column: that is a
/// question about today, and a stored answer would be wrong for a year at a
/// time.
/// </summary>
public sealed class NhlSeason
{
    /// <summary>The NHL season identifier and primary key, e.g. "20262027".</summary>
    public required string Season { get; set; }

    /// <summary>First day of the regular season, ET game date.</summary>
    public DateOnly RegularSeasonStart { get; set; }

    /// <summary>Last day of the regular season, ET game date.</summary>
    public DateOnly RegularSeasonEnd { get; set; }

    /// <summary>
    /// First day of the playoffs. Null until the NHL publishes it — the regular
    /// season's end date is known months before the playoff bracket is.
    ///
    /// Nothing scores playoffs today (<c>GameType == 2</c> is filtered
    /// everywhere, by rule), so this is a date the app records rather than one
    /// it acts on.
    /// </summary>
    public DateOnly? PlayoffStart { get; set; }

    /// <summary>Last possible day of the playoffs. Null for the same reason.</summary>
    public DateOnly? PlayoffEnd { get; set; }

    /// <summary>
    /// When <c>stats-sync</c> last imported games for this season, or null if it
    /// never has. Distinguishes "the schedule is published and we have it" from
    /// "we only know the dates someone typed in", which is exactly the
    /// difference <c>period-init</c> has to reason about.
    /// </summary>
    public DateTime? ScheduleImportedUtc { get; set; }
}
