namespace FantasyWarrior.Core.Rules;

/// <summary>
/// One rule a commissioner has set that the code does not act on yet.
/// </summary>
/// <param name="Path">
/// The document path, e.g. <c>freeAgency.mode</c> — what a rules panel keys its
/// badge off, and what a consumer names when it refuses.
/// </param>
/// <param name="Message">Plain language: what is recorded, and what actually happens.</param>
public readonly record struct RuleGap(string Path, string Message);

/// <summary>
/// Which values of a <see cref="RuleSet"/> the application actually honours.
/// Pure.
///
/// <b>By value, not by field.</b> "Do playoffs score" is supported at
/// <c>false</c> and not at <c>true</c>; a field-level answer could not say that.
/// So this takes a whole configuration and reports the specific values in it
/// that nothing enforces.
///
/// <b>This is not validation.</b> A gap does not stop a save — a commissioner is
/// allowed to record the pool's real rules before the code catches up, and the
/// rules panel badges them. What a gap must never do is pass silently: the bug
/// this whole design replaces was <c>StealRounds</c> sitting <c>NULL</c> while
/// <c>draft/open</c> read it as <c>?? 0</c> and opened a draft with no steal
/// segment. <b>A consumer meeting a value listed here refuses the action and
/// names it.</b> It never falls back to a default.
///
/// Removing an entry from this file is how a feature ships: the wiring and the
/// claim that it exists live one function apart, so they cannot drift.
/// </summary>
public static class RuleSetCapabilities
{
    /// <summary>Every rule in this configuration that nothing enforces; empty means all of it is live.</summary>
    public static IReadOnlyList<RuleGap> Unsupported(RuleSet rules)
    {
        var gaps = new List<RuleGap>();

        if (rules.PoolType is PoolType.SingleSeason)
            gaps.Add(new RuleGap("poolType",
                "Rosters carry over between seasons whatever this says. Nothing clears them for a "
                + "redraft, and no season-opening draft of a whole roster exists."));

        if (rules.Cap.Min is not null)
            gaps.Add(new RuleGap("cap.min",
                "The cap floor is recorded but never checked. Only the ceiling is enforced, on "
                + "trades and on draft selections."));

        if (!rules.Roster.ByPosition.IsEmpty)
            gaps.Add(new RuleGap("roster.byPosition",
                "Per-position roster bounds are recorded but not enforced. Only the overall "
                + "minimum and maximum are, and only on trades."));

        if (rules.Lineup.Mode is LineupMode.TopN)
            gaps.Add(new RuleGap("lineup.mode",
                "Scoring still counts the players the GM activated, not the best N per position. "
                + "The weekly lineup screen and its Monday lock stay in force."));

        if (rules.Lineup.OnMissing is MissingLineupBehaviour.ScoreZero)
            gaps.Add(new RuleGap("lineup.onMissing",
                "A forgotten lineup is still carried forward from the previous week and topped "
                + "up. Nothing scores a team zero for not submitting."));

        if (rules.Scoring.ByPosition.Count > 0)
            gaps.Add(new RuleGap("scoring.byPosition",
                "Per-position point values are recorded but not applied. Every player scores "
                + "under the league's general scale."));

        if (rules.Scoring.IncludePlayoffs)
            gaps.Add(new RuleGap("scoring.includePlayoffs",
                "Playoff games do not score. Regular season only is filtered in the rollup job, "
                + "the views and the API reads alike."));

        if (rules.Trades.Approval is not TradeApproval.None)
            gaps.Add(new RuleGap("trades.approval",
                "A trade executes as soon as both GMs agree. League votes are recorded and rated "
                + "but block nothing, and there is no commissioner veto."));

        if (rules.Trades.PickYearsAhead > 1)
            gaps.Add(new RuleGap("trades.pickYearsAhead",
                "Picks exist one season ahead only — draft-picks-init generates a single "
                + "season's — so nothing further out can be traded."));

        if (rules.Protection.SlotsByPosition is not null)
            gaps.Add(new RuleGap("protection.slotsByPosition",
                "Protection slots are counted as one pool. A per-position cap is recorded but "
                + "neither the autofill nor the draft reads it."));

        if (rules.Protection.AfterDraft is AfterDraftDisposition.ReleasedToFreeAgents)
            gaps.Add(new RuleGap("protection.afterDraft",
                "An exposed player nobody claimed stays on his team, having never moved. "
                + "Releasing him needs free agency, which does not exist."));

        if (rules.Draft.UnprotectedDisposition is UnprotectedDisposition.OpenPool)
            gaps.Add(new RuleGap("draft.unprotectedDisposition",
                "The rookie segment offers unrostered players only. Putting unprotected players "
                + "into that same pool is not implemented."));

        if (rules.Draft.Steal.TurnsTradable)
            gaps.Add(new RuleGap("draft.steal.turnsTradable",
                "A steal turn is derived from the standings and owns no row, so there is nothing "
                + "to trade. Every team gets the same number."));

        if (rules.Draft.Snake)
            gaps.Add(new RuleGap("draft.snake",
                "The draft order is linear: the same reverse-standings order every round, frozen "
                + "when the room opens."));

        if (rules.FreeAgency.Mode is not FreeAgencyMode.None)
            gaps.Add(new RuleGap("freeAgency.mode",
                "There is no add or drop path. The free-agent list is a read-only leaderboard "
                + "ranked under the league's own scale."));

        return gaps;
    }

    /// <summary>Does the app honour every rule in this configuration?</summary>
    public static bool IsFullySupported(RuleSet rules) => Unsupported(rules).Count == 0;

    /// <summary>
    /// The gap at exactly this path, if any — how a consumer checks the one rule
    /// it is about to act on before acting on it.
    /// </summary>
    public static RuleGap? GapAt(RuleSet rules, string path)
    {
        foreach (var gap in Unsupported(rules))
            if (gap.Path == path) return gap;
        return null;
    }
}
