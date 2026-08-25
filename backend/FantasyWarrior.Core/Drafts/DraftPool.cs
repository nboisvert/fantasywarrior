namespace FantasyWarrior.Core.Drafts;

/// <summary>
/// One player as the pool rule needs to see him. Primitives only: the database
/// assembles these, this file decides.
/// </summary>
/// <param name="CareerNhlGames">
/// Null means <b>never synced</b>, not zero — see <c>Player.CareerNhlGames</c>.
/// The distinction is the whole reason this is nullable here.
/// </param>
/// <param name="OwnerTeamId">Null when nobody in the league holds him.</param>
/// <param name="ProtectedByGm">
/// His GM spent a protection slot on him. Distinct from auto-protection, which
/// is free and derived — see <see cref="ProtectionRules"/>.
/// </param>
/// <param name="AlreadyTakenThisDraft">
/// He has already changed hands in this draft. Enforced by a unique index too,
/// but it belongs here as well: without it the pool would offer a row that the
/// database then refuses, and a GM would be told "no" only after tapping.
/// </param>
/// <param name="OwnerLossesSoFar">
/// How many players his team has already lost in this draft. It is on the
/// candidate rather than passed alongside because the quota closes the pool
/// <i>per owner</i>, and flattening it here is what lets the rule stay a
/// per-player predicate.
/// </param>
public sealed record DraftCandidate(
    long PlayerId,
    string PositionGroup,
    int? CareerNhlGames,
    int? OwnerTeamId,
    bool ProtectedByGm,
    int OwnerLossesSoFar,
    bool AlreadyTakenThisDraft = false);

/// <summary>
/// Who may be taken, and why not. <b>This is the class that makes the draft
/// generic over its type</b>: one turn engine, one selection log, and a pool
/// strategy chosen per <see cref="DraftSegment"/>. Adding an initial draft
/// later is a third branch here and nothing else.
///
/// The rule is evaluated per turn and never cached. It cannot be: the
/// "max losses per team" quota closes a whole roster out of the pool the moment
/// its team hits the limit, so the answer for a given player changes as other
/// people pick.
/// </summary>
public static class DraftPool
{
    /// <summary>
    /// Why this player cannot be taken on this turn, or null when he can.
    ///
    /// Returning the reason rather than a bool is deliberate: the same string
    /// words the 400 the API returns and the tooltip the room shows, so a GM is
    /// never told "no" without being told why.
    /// </summary>
    public static string? IneligibleReason(
        DraftCandidate candidate, DraftSegment segment, int pickingTeamId, int? maxLossesPerTeam)
    {
        // The Équipe slot holds a franchise, not a player. It may only ever move
        // against another franchise (see TradeRules.ValidateFranchiseBalance),
        // so a draft has no way to take one — scoring-model.md §11 lists this
        // exclusion as a thing the draft owes.
        if (candidate.PositionGroup == "T")
            return "A franchise cannot be drafted.";

        // A player moves at most once per draft, in either segment. The unique
        // index on (LeagueSeasonId, PlayerId) enforces it; saying so here is
        // what keeps him off the list in the first place.
        if (candidate.AlreadyTakenThisDraft)
            return "He has already been drafted this off-season.";

        return segment switch
        {
            DraftSegment.Steal => StealReason(candidate, pickingTeamId, maxLossesPerTeam),
            DraftSegment.Rookie => RookieReason(candidate),
            _ => "Unknown draft segment.",
        };
    }

    private static string? StealReason(DraftCandidate c, int pickingTeamId, int? maxLossesPerTeam)
    {
        if (c.OwnerTeamId is null)
            return "He is unrostered — he belongs to the rookie rounds, not the steal rounds.";

        if (c.OwnerTeamId == pickingTeamId)
            return "You already hold him.";

        if (c.ProtectedByGm)
            return "His GM protected him.";

        // An unknown career total is not a zero, and IsAutoProtected refuses to
        // be handed a guess. Refusing here is the only answer that cannot hand
        // someone an untouchable prospect by accident: getting it wrong this way
        // withholds one player from a pool of hundreds, getting it wrong the
        // other way steals a player the rules said was safe.
        if (c.CareerNhlGames is not { } games)
            return "His NHL experience is unknown, so he cannot be drafted.";

        if (ProtectionRules.IsAutoProtected(c.PositionGroup, games))
            return "He is auto-protected — too few career NHL games.";

        if (maxLossesPerTeam is { } max && c.OwnerLossesSoFar >= max)
            return $"His team has already lost {max}.";

        return null;
    }

    private static string? RookieReason(DraftCandidate c) =>
        // Protection and career games say nothing here: they govern who may be
        // taken *away from a GM*, and nobody holds this player.
        c.OwnerTeamId is not null ? "He is already on a roster." : null;

    /// <summary>Whether this player may be taken on this turn.</summary>
    public static bool IsEligible(
        DraftCandidate candidate, DraftSegment segment, int pickingTeamId, int? maxLossesPerTeam) =>
        IneligibleReason(candidate, segment, pickingTeamId, maxLossesPerTeam) is null;

    /// <summary>
    /// The pool for one turn. Input order is preserved — the caller has already
    /// sorted it the way the screen wants, and re-sorting here would silently
    /// override that.
    /// </summary>
    public static IReadOnlyList<DraftCandidate> Available(
        IEnumerable<DraftCandidate> candidates,
        DraftSegment segment,
        int pickingTeamId,
        int? maxLossesPerTeam) =>
        candidates
            .Where(c => IsEligible(c, segment, pickingTeamId, maxLossesPerTeam))
            .ToList();
}
