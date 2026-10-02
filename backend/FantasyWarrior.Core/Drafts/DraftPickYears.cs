namespace FantasyWarrior.Core.Drafts;

/// <summary>
/// Which draft years hold picks while a season is played. Pure.
///
/// Picks exist for the next draft and for each year after it up to
/// <c>trades.pickYearsAhead</c>, and for nothing further — which is what makes
/// "tradable N years in advance" hold without a rule enforcing it: no other year
/// exists to trade.
/// </summary>
public static class DraftPickYears
{
    public static IReadOnlyList<int> Ahead(int nextDraftYear, int pickYearsAhead) =>
        Enumerable.Range(nextDraftYear, Math.Max(1, pickYearsAhead)).ToList();
}
