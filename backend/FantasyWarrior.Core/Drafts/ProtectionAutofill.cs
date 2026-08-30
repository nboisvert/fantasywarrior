using FantasyWarrior.Core.Rules;

namespace FantasyWarrior.Core.Drafts;

/// <summary>
/// One roster spot as the autofill needs to see it. Primitives only, same
/// posture as <see cref="DraftCandidate"/>: the database assembles these, this
/// file decides.
/// </summary>
/// <param name="Points">
/// What the player produced last season <b>under this league's own scale</b>,
/// not raw NHL points. The caller owes that conversion, and it is not a detail:
/// scored on goals and assists alone, a goalie's season is zero and no GM would
/// ever protect one. Same argument as <see cref="Scoring.FreeAgentRanking"/>.
/// </param>
/// <param name="CareerNhlGames">
/// Null means <b>never synced</b>, not zero — see <c>Player.CareerNhlGames</c>.
/// </param>
public sealed record ProtectionCandidate(
    int RosterSpotId,
    int TeamId,
    long PlayerId,
    string PositionGroup,
    int? CareerNhlGames,
    double Points);

/// <summary>
/// Which spots a GM would protect if he simply kept his best players. Pure.
///
/// <b>What this is for.</b> The protection screen is not built, so nothing can
/// write <c>Protected</c> and a draft held today would filter on auto-protection
/// alone. This fills the slate with the obvious answer — the top scorers of each
/// roster — so the room can be exercised against a realistic pool. It is a
/// default, not a decision: a GM who disagrees is exactly what the real screen
/// will be for.
///
/// <b>A slot is only spent on someone who could actually be taken.</b> An
/// auto-protected prospect is already out of reach for free
/// (<see cref="ProtectionRules"/>), so spending a slot on him would throw it
/// away. The same goes for a player whose NHL experience was never synced:
/// <see cref="DraftPool"/> already refuses to draft him, so he needs no slot
/// either. Both are simply not candidates — neither is marked protected, because
/// neither needs to be.
/// </summary>
public static class ProtectionAutofill
{
    /// <summary>
    /// The <c>RosterSpotId</c>s to mark <c>Protected</c>, across every team.
    ///
    /// Ties break on <c>PlayerId</c> ascending so two runs over the same data
    /// choose the same men. The endpoint clears and rewrites the whole league,
    /// so a run that was not reproducible would quietly reshuffle who is exposed
    /// every time the commissioner pressed the button.
    /// </summary>
    /// <param name="slots">
    /// The league's <c>protection.slots</c>. Zero protects nobody, which is a
    /// real configuration and not an error.
    /// </param>
    /// <param name="auto">
    /// The league's auto-protection bars. A slot spent on someone the draft
    /// already cannot take is a slot wasted, and it would expose the veteran it
    /// should have covered — so these decide who is even a candidate.
    /// </param>
    public static IReadOnlyList<int> Choose(
        IEnumerable<ProtectionCandidate> candidates, int slots, AutoProtectConfig auto)
    {
        if (slots <= 0) return [];

        return candidates
            .Where(c => NeedsASlot(c, auto))
            .GroupBy(c => c.TeamId)
            .SelectMany(team => team
                .OrderByDescending(c => c.Points)
                .ThenBy(c => c.PlayerId)
                .Take(slots))
            .Select(c => c.RosterSpotId)
            .ToList();
    }

    /// <summary>
    /// Is this man exposed unless a slot is spent on him? False for everyone the
    /// draft already cannot touch.
    /// </summary>
    public static bool NeedsASlot(ProtectionCandidate c, AutoProtectConfig auto) =>
        // A franchise may only ever move against another franchise, so the draft
        // has no way to take one — DraftPool says the same thing first.
        c.PositionGroup != "T"
        && c.CareerNhlGames is { } games
        && !ProtectionRules.IsAutoProtected(c.PositionGroup, games, auto);
}
