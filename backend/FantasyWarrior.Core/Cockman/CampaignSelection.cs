namespace FantasyWarrior.Core.Cockman;

public sealed record CampaignCandidate(int CampaignId, DateTime StartUtc, DateTime? EndUtc);

/// <summary>
/// Which Cockman campaign, if any, to show a user right now. Pure — no DB, no
/// clock reads — so the "don't dump a backlog on a new user" rule is
/// unit-testable on its own.
/// </summary>
public static class CampaignSelection
{
    /// <summary>
    /// The one campaign to show now, or null. A campaign is eligible when its
    /// window is currently open (StartUtc &lt;= now &lt;= EndUtc, EndUtc null
    /// means forever) and the user hasn't seen it yet. Never more than one at
    /// a time — earliest StartUtc wins, ties broken by the lowest campaign id
    /// for determinism. A campaign whose window has already closed is simply
    /// never eligible, which is what stops a brand-new user from being shown
    /// every past campaign at once.
    /// </summary>
    public static int? SelectNext(
        IEnumerable<CampaignCandidate> candidates, IReadOnlyCollection<int> seenCampaignIds, DateTime nowUtc)
    {
        var seen = seenCampaignIds as ISet<int> ?? seenCampaignIds.ToHashSet();
        return candidates
            .Where(c => !seen.Contains(c.CampaignId))
            .Where(c => c.StartUtc <= nowUtc && (c.EndUtc is null || nowUtc <= c.EndUtc))
            .OrderBy(c => c.StartUtc)
            .ThenBy(c => c.CampaignId)
            .Select(c => (int?)c.CampaignId)
            .FirstOrDefault();
    }
}
