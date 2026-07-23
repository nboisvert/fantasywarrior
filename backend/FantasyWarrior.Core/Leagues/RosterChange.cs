using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// Applies a roster change to one team: computes the transaction-invariant
/// adjustment (score must not jump at transaction time), closes any
/// outgoing players' open assignments (freezing their per-stint stats one
/// final time), opens new assignments for incoming players, and persists
/// the team doc's derived fields. Generalizes the single-player add/drop
/// endpoints to N-out/M-in so a two-sided trade can call this once per team.
///
/// Lives in Core (not Api) so both the API's add/drop/trade endpoints and
/// the Jobs project's nightly trade-processing job can share it.
/// </summary>
public static class RosterChange
{
    /// <summary>
    /// Pure: the new Assignment docs to create for players joining a team —
    /// no Firestore I/O, so this is unit-testable on its own (e.g. "does
    /// processing a trade open an assignment per incoming player, tagged
    /// with the trade's id as sourceRefId").
    /// </summary>
    public static List<Assignment> BuildOpenedAssignments(
        IReadOnlyCollection<long> playersIn, string teamUsername, string source, string? sourceRefId,
        string effectiveDate, Timestamp createdUtc) =>
        playersIn.Select(playerId => new Assignment
        {
            PlayerId = playerId,
            TeamUsername = teamUsername,
            From = effectiveDate,
            Source = source,
            SourceRefId = sourceRefId,
            CreatedUtc = createdUtc,
        }).ToList();

    /// <summary>
    /// Pure: the field map to write when closing an outgoing player's open
    /// assignment — freezes the per-stint stats one final time (via
    /// <see cref="AssignmentStats.ToFieldMap"/>), sets `to`/`closedUtc`, and
    /// records why it closed (`closeReason`/`closeSourceRefId`) separately
    /// from `source` (why it was originally opened) — a season-long
    /// "initial" assignment ending via a trade must show up as a trade in
    /// the activity feed's drop event, not as "initial".
    /// </summary>
    public static Dictionary<string, object> BuildClosedAssignmentFields(
        PlayerRawTotals finalTotals, double finalFantasyPoints, string effectiveDate, Timestamp closedUtc,
        string closeReason, string? closeSourceRefId)
    {
        var fields = AssignmentStats.ToFieldMap(finalTotals, finalFantasyPoints, closedUtc);
        fields["to"] = effectiveDate;
        fields["closedUtc"] = closedUtc;
        fields["closeReason"] = closeReason;
        if (closeSourceRefId is not null)
            fields["closeSourceRefId"] = closeSourceRefId;
        return fields;
    }

    /// <summary>Pure: a team's roster after removing playersOut and adding playersIn.</summary>
    public static List<long> BuildNewPlayerIds(
        IReadOnlyCollection<long> currentPlayerIds, IReadOnlyCollection<long> playersOut, IReadOnlyCollection<long> playersIn)
    {
        var outSet = playersOut.ToHashSet();
        return currentPlayerIds.Where(id => !outSet.Contains(id)).Concat(playersIn).ToList();
    }

    public static async Task ApplyAsync(
        FirestoreDb db,
        DocumentReference leagueDoc,
        League league,
        DocumentReference teamDoc,
        Team team,
        IReadOnlyDictionary<long, string> positions,
        IReadOnlyCollection<long> playersOut,
        IReadOnlyCollection<long> playersIn,
        string reason,
        string source,
        string? sourceRefId,
        string effectiveDate,
        CancellationToken ct = default)
    {
        var entriesBefore = team.PlayerIds
            .Select(id => (id, positions.GetValueOrDefault(id, "C"), team.PlayerPoints.GetValueOrDefault(id.ToString(), 0)))
            .ToList();

        Dictionary<long, PlayerRawTotals> incomingTotals = playersIn.Count > 0
            ? await PlayerTotalsSource.FetchAsync(db, playersIn, league.Season, ct)
            : [];
        var incomingPoints = playersIn.ToDictionary(
            id => id,
            id => ScoringEngine.PlayerPoints(incomingTotals.GetValueOrDefault(id) ?? new PlayerRawTotals(), league.RuleConfig.PointValues));

        var outSet = playersOut.ToHashSet();
        var entriesAfter = entriesBefore
            .Where(e => !outSet.Contains(e.Item1))
            .Concat(playersIn.Select(id => (id, positions.GetValueOrDefault(id, "C"), incomingPoints[id])))
            .ToList();

        var before = ScoringEngine.TeamScoreFromPoints(entriesBefore, league.RuleConfig.TopCount);
        var after = ScoringEngine.TeamScoreFromPoints(entriesAfter, league.RuleConfig.TopCount);
        var delta = ScoringEngine.TransactionAdjustment(before.RawTopX, after.RawTopX);

        if (delta != 0)
            await teamDoc.Collection("adjustments").AddAsync(new Adjustment
            {
                Delta = delta,
                Reason = reason,
                PlayerIdsIn = [.. playersIn],
                PlayerIdsOut = [.. playersOut],
                DateUtc = Timestamp.GetCurrentTimestamp(),
            }, ct);

        // Close outgoing assignments, freezing per-stint stats one final time
        // (they stop refreshing nightly the moment `to` is set).
        var closeNow = Timestamp.GetCurrentTimestamp();
        foreach (var playerId in playersOut)
        {
            var openSnap = await leagueDoc.Collection("assignments")
                .WhereEqualTo("playerId", playerId)
                .WhereEqualTo("teamUsername", team.OwnerUsername)
                .WhereEqualTo("to", null)
                .GetSnapshotAsync(ct);
            foreach (var assignmentDoc in openSnap.Documents)
            {
                var a = assignmentDoc.ConvertTo<Assignment>();
                var lines = await PlayerTotalsSource.FetchLinesAsync(db, playerId, ct);
                var finalTotals = PlayerTotalsSource.AggregateRange(lines, league.Season, a.From, effectiveDate);
                var finalFantasyPoints = ScoringEngine.PlayerPoints(finalTotals, league.RuleConfig.PointValues);
                var fields = BuildClosedAssignmentFields(finalTotals, finalFantasyPoints, effectiveDate, closeNow, source, sourceRefId);
                await assignmentDoc.Reference.UpdateAsync(fields, cancellationToken: ct);
            }
        }

        // Open incoming assignments.
        foreach (var assignment in BuildOpenedAssignments(playersIn, team.OwnerUsername, source, sourceRefId, effectiveDate, Timestamp.GetCurrentTimestamp()))
            await leagueDoc.Collection("assignments").AddAsync(assignment, ct);

        var newPlayerIds = BuildNewPlayerIds(team.PlayerIds, playersOut, playersIn);
        var adjustmentsTotal = team.AdjustmentsTotal + delta;
        var fieldUpdates = new Dictionary<string, object>
        {
            ["playerIds"] = newPlayerIds,
            ["rawTopXScore"] = after.RawTopX,
            ["adjustmentsTotal"] = adjustmentsTotal,
            ["score"] = after.RawTopX + adjustmentsTotal,
            ["countedPlayerIds"] = after.CountedPlayerIds.ToList(),
        };
        foreach (var id in playersOut)
            fieldUpdates[$"playerPoints.{id}"] = FieldValue.Delete;
        foreach (var id in playersIn)
            fieldUpdates[$"playerPoints.{id}"] = incomingPoints[id];

        await teamDoc.UpdateAsync(fieldUpdates, cancellationToken: ct);
    }
}
