namespace FantasyWarrior.Core.Drafts;

/// <summary>
/// One rookie-segment entitlement, as the turn engine needs to see it.
/// </summary>
/// <param name="CurrentTeamId">
/// Who picks — the <b>current</b> holder, not the original one. A rookie pick is
/// tradable, so this is the whole reason the two segments cannot share an
/// ordering rule.
/// </param>
public sealed record PickSlot(
    int DraftPickId,
    int Round,
    int PickInRound,
    int CurrentTeamId,
    bool Used);

/// <summary>
/// Whose turn it is, and in what order. Pure — no entity, no database, no clock.
///
/// <b>There is no pick clock</b> (Nick, 2026-08-25). The draft is asynchronous:
/// the GM on the clock picks whenever they get to it and everyone waits. That
/// removes an entire dimension from this class — no deadline, no expiry, no
/// auto-pick — and leaves "whose turn is it" as a pure function of one number:
/// how many selections have already been made.
///
/// <b>The two segments read their order from different places, on purpose.</b>
/// Both are frozen into <c>DraftPick.PickInRound</c> when the draft opens, but:
/// <list type="bullet">
///   <item>steal turns follow the pick's <c>OriginalTeamId</c> — a GM who
///   trades away a first-round rookie pick must not lose his steal turn with
///   it, because steal turns were never his to trade;</item>
///   <item>rookie turns follow <c>CurrentTeamId</c> — that is the entitlement
///   actually changing hands.</item>
/// </list>
/// One frozen ordering, two readings. The alternative — re-reading the
/// standings on every request — is a latent bug: <c>SeasonPhaseJob</c> advances
/// <c>Leagues.Season</c> on entry to <c>InSeason</c>, after which
/// <c>vStandings</c> reports the new season and reverse standings would quietly
/// become meaningless.
/// </summary>
public static class DraftOrder
{
    /// <summary>
    /// Reverse standings: the worst team picks first.
    ///
    /// <b>Ties break on <c>TeamId</c>, ascending.</b> Not a fairness rule — an
    /// arbitrary but *stable* one. Two teams on the same score are perfectly
    /// possible in a pool this size, and without a deterministic tiebreak the
    /// draft order would depend on whatever order the database happened to
    /// return rows in, differing between two reads of the same standings.
    /// </summary>
    public static IReadOnlyList<int> ReverseStandings(IEnumerable<(int TeamId, double Score)> standings) =>
        standings
            .OrderBy(s => s.Score)
            .ThenBy(s => s.TeamId)
            .Select(s => s.TeamId)
            .ToList();

    /// <summary>
    /// The steal turn at this point in the draft, or null once the steal
    /// segment is over.
    /// </summary>
    public static DraftTurn? StealTurn(
        IReadOnlyList<int> orderedTeamIds, int stealRounds, int selectionsMade)
    {
        var teamCount = orderedTeamIds.Count;
        var stealTurns = DraftSegments.StealTurnCount(teamCount, stealRounds);
        if (selectionsMade < 0 || selectionsMade >= stealTurns) return null;

        var pickInRound = DraftSegments.StealPickInRound(selectionsMade, teamCount);

        return new DraftTurn(
            Segment: DraftSegment.Steal,
            OverallIndex: selectionsMade,
            Round: DraftSegments.StealRound(selectionsMade, teamCount),
            PickInRound: pickInRound,
            TeamId: orderedTeamIds[pickInRound - 1],
            DraftPickId: null);
    }

    /// <summary>
    /// The rookie turn at this point in the draft, or null once every
    /// entitlement has been spent.
    ///
    /// Unused picks are taken in <c>(Round, PickInRound)</c> order and used ones
    /// are skipped, so this stays correct even if a selection was made out of
    /// band. It reads the picks rather than counting them for the same reason:
    /// the count of selections tells us how far along the draft is, but only the
    /// rows say which entitlement is next.
    /// </summary>
    public static DraftTurn? RookieTurn(
        IEnumerable<PickSlot> picks, int stealTurnCount, int selectionsMade)
    {
        if (selectionsMade < stealTurnCount) return null;

        var next = picks
            .Where(p => !p.Used)
            .OrderBy(p => p.Round)
            .ThenBy(p => p.PickInRound)
            .FirstOrDefault();

        if (next is null) return null;

        return new DraftTurn(
            Segment: DraftSegment.Rookie,
            OverallIndex: selectionsMade,
            Round: next.Round,
            PickInRound: next.PickInRound,
            TeamId: next.CurrentTeamId,
            DraftPickId: next.DraftPickId);
    }

    /// <summary>
    /// The one call an endpoint makes. Null means the draft is finished — every
    /// steal turn taken and every entitlement spent.
    /// </summary>
    public static DraftTurn? OnTheClock(
        IReadOnlyList<int> orderedTeamIds,
        int stealRounds,
        IEnumerable<PickSlot> picks,
        int selectionsMade)
    {
        var stealTurns = DraftSegments.StealTurnCount(orderedTeamIds.Count, stealRounds);

        return selectionsMade < stealTurns
            ? StealTurn(orderedTeamIds, stealRounds, selectionsMade)
            : RookieTurn(picks, stealTurns, selectionsMade);
    }

    /// <summary>
    /// How many turns until this team picks again — 0 when it is their turn
    /// right now, null when they have none left.
    ///
    /// This is what lets the room say "you pick in 3" instead of only "not your
    /// turn", which is the difference between a GM closing the tab and a GM
    /// waiting.
    /// </summary>
    public static int? TurnsUntil(
        IReadOnlyList<int> orderedTeamIds,
        int stealRounds,
        IEnumerable<PickSlot> picks,
        int selectionsMade,
        int teamId)
    {
        var remaining = picks.Where(p => !p.Used).ToList();
        var stealTurns = DraftSegments.StealTurnCount(orderedTeamIds.Count, stealRounds);

        // Walking the plan forward rather than computing an offset: the two
        // segments index differently, and a closed-form expression spanning the
        // boundary would be the kind of arithmetic nobody can check by reading.
        var cursor = selectionsMade;
        var total = stealTurns + remaining.Count;

        while (cursor < total)
        {
            var turn = OnTheClock(orderedTeamIds, stealRounds, remaining, cursor);
            if (turn is null) return null;
            if (turn.TeamId == teamId) return cursor - selectionsMade;

            if (turn.Segment == DraftSegment.Rookie)
                remaining.RemoveAll(p => p.DraftPickId == turn.DraftPickId);

            cursor++;
        }

        return null;
    }
}
