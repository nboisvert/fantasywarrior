namespace FantasyWarrior.Core.Seasons;

/// <summary>An inclusive span of ET game dates.</summary>
public readonly record struct SeasonWindow(DateOnly Start, DateOnly End)
{
    /// <summary>The span covering both, i.e. the earlier start and the later end.</summary>
    public SeasonWindow Union(SeasonWindow other) => new(
        Start <= other.Start ? Start : other.Start,
        End >= other.End ? End : other.End);
}

/// <summary>
/// Reconciles the two things that can say when a season runs. Pure — no I/O, no
/// clock.
///
/// <b>Declared</b> bounds come from the <c>Seasons</c> row: the schedule the NHL
/// published, known months before a puck drops. <b>Observed</b> bounds come from
/// the <c>Games</c> already imported. Either can be missing, and they can
/// legitimately disagree: mid-season the observed end is only as late as the
/// last game played, and a rescheduled game can fall outside what was declared.
///
/// <b>The union is the only safe merge.</b> The weekly calendar is what assigns
/// a game day to a scoring week, so a game day outside every period would score
/// for nobody, silently — the one failure this function exists to prevent.
/// Taking the declared bounds alone would drop a game rescheduled past the
/// published end; taking the observed alone would rebuild the very restriction
/// that kept next season's calendar from existing.
///
/// The cost is empty weeks at the front of a season whose schedule is not
/// imported yet. That is a display concern (<c>Period.GameCount</c>), not a
/// scoring one, and it is why <c>period-init</c> refreshes the count on weeks it
/// has already created while never touching their boundaries.
/// </summary>
public static class SeasonBounds
{
    /// <summary>
    /// The span a season's weekly calendar must cover, or null when neither
    /// source knows anything — the only case a caller has to refuse.
    /// </summary>
    public static SeasonWindow? Resolve(SeasonWindow? declared, SeasonWindow? observed) =>
        (declared, observed) switch
        {
            ({ } d, { } o) => d.Union(o),
            ({ } d, null) => d,
            (null, { } o) => o,
            _ => null,
        };
}
