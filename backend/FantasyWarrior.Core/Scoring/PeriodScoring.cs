namespace FantasyWarrior.Core.Scoring;

/// <summary>The result of folding a finished period into a running total.</summary>
public readonly record struct FinalizeResult(int FinalizedThroughPeriodIndex, double FinalizedPoints);

/// <summary>
/// The pure arithmetic of period-based scoring: what to bank, when to bank it,
/// and when to leave it alone.
/// </summary>
public static class PeriodScoring
{
    /// <summary>
    /// Folds a finished period into a running banked total, or returns null if
    /// it has already been folded in.
    ///
    /// This guard is the difference between correct standings and silently
    /// corrupt ones. The tempting shape is <c>finalized += periodPoints</c>,
    /// which double-counts the moment anything re-runs — a retried GitHub
    /// Actions job, a manual catch-up, a crash between two writes. Storing
    /// <c>finalizedThroughPeriodIndex</c> next to the total and writing both in
    /// the same single-document update (atomic in Firestore) makes a re-run a
    /// no-op by construction rather than by discipline.
    /// </summary>
    public static FinalizeResult? ApplyFinalize(
        int finalizedThroughPeriodIndex, int periodIndex, double finalizedPoints, double periodPoints)
    {
        if (periodIndex <= finalizedThroughPeriodIndex) return null;
        return new FinalizeResult(periodIndex, finalizedPoints + periodPoints);
    }

    /// <summary>
    /// Whether a period's points can be banked yet.
    ///
    /// The grace day exists because the NHL corrects boxscores after the fact.
    /// Finalizing the moment the week ends would bank whatever was known that
    /// night and never revisit it, so a correction filed the next morning would
    /// be lost silently.
    /// </summary>
    public static bool ShouldFinalize(DateOnly periodEndDate, DateOnly lastStatDate, int graceDays = 1) =>
        lastStatDate >= periodEndDate.AddDays(graceDays);

    /// <summary>
    /// The "YYYY-MM-DD" form, for the Firestore path that still stores dates as
    /// strings. Parses and delegates, so the rule has one implementation — this
    /// overload disappears with that path.
    /// </summary>
    public static bool ShouldFinalize(string periodEndDate, string lastStatDate, int graceDays = 1) =>
        ShouldFinalize(DateOnly.Parse(periodEndDate), DateOnly.Parse(lastStatDate), graceDays);

    /// <summary>
    /// A team's live score: what is banked, plus what the current period has
    /// produced so far.
    ///
    /// Always computed absolutely, never accumulated — recomputing the current
    /// period from scratch each night is what makes the whole nightly job safe
    /// to re-run.
    /// </summary>
    public static double LiveScore(double finalizedScore, double currentPeriodPoints) =>
        finalizedScore + currentPeriodPoints;

    /// <summary>
    /// Whether a scoring result differs from what is already stored.
    ///
    /// Most roster spots don't change on most nights — a player with no game
    /// produces the same numbers as yesterday. Skipping those writes takes a
    /// league's nightly writes from ~180 to ~60.
    /// </summary>
    public static bool NeedsWrite(double storedPoints, int storedGamesPlayed, double points, int gamesPlayed) =>
        storedPoints != points || storedGamesPlayed != gamesPlayed;
}
