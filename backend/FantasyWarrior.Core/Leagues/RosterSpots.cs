using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// Building and finding <see cref="RosterSpot"/> documents. The pure builders
/// carry the logic worth testing; the query helper exists so one easily
/// forgotten rule can't be forgotten.
/// </summary>
public static class RosterSpots
{
    public static CollectionReference Collection(DocumentReference leagueDoc) =>
        leagueDoc.Collection("rosterSpots");

    /// <summary>Pure: the spots to open for players joining a team.</summary>
    public static List<RosterSpot> BuildOpened(
        IReadOnlyCollection<long> playersIn,
        string teamUsername,
        IReadOnlyDictionary<long, string> positions,
        string startReason,
        string? startRefId,
        string effectiveDate,
        Timestamp openedUtc) =>
        playersIn.Select(playerId =>
        {
            var position = positions.GetValueOrDefault(playerId, "C");
            return new RosterSpot
            {
                PlayerId = playerId,
                TeamUsername = teamUsername,
                Position = position,
                PositionGroup = PositionGroups.CodeFrom(position),
                StartDate = effectiveDate,
                StartReason = startReason,
                StartRefId = startRefId,
                OpenedUtc = openedUtc,
            };
        }).ToList();

    /// <summary>
    /// Pure: the fields to write when a player leaves. Closing records why the
    /// spot ended, kept separate from why it opened — a player acquired as a
    /// free agent and later traded away must read as a trade on the way out.
    ///
    /// Note it writes no stats. Unlike the legacy Assignment, a spot's totals
    /// are a rollup of its weekly lineups and are recomputed by the scoring
    /// job; there is nothing to freeze here.
    /// </summary>
    public static Dictionary<string, object> BuildClosedFields(
        string effectiveDate, Timestamp closedUtc, string endReason, string? endRefId)
    {
        var fields = new Dictionary<string, object>
        {
            ["endDate"] = effectiveDate,
            ["closedUtc"] = closedUtc,
            ["endReason"] = endReason,
        };
        if (endRefId is not null) fields["endRefId"] = endRefId;
        return fields;
    }

    /// <summary>
    /// Every spot that owns any part of a period — open spots, plus spots that
    /// closed during it.
    ///
    /// The second half is the one that gets forgotten. Querying only
    /// <c>endDate == null</c> looks right and is wrong: a spot closed on the
    /// Thursday still owns Monday through Wednesday, and those points are not
    /// banked yet. Miss it and every team's score visibly drops each night
    /// until the period finalizes, then jumps back.
    ///
    /// Firestore has no OR across different operators, so this is a union of
    /// two queries, deduplicated by document id.
    /// </summary>
    public static async Task<List<(DocumentReference Ref, RosterSpot Spot)>> RelevantToAsync(
        DocumentReference leagueDoc, string periodStartDate, CancellationToken ct = default)
    {
        var col = Collection(leagueDoc);
        var open = await col.WhereEqualTo("endDate", null).GetSnapshotAsync(ct);
        var closedDuring = await col.WhereGreaterThanOrEqualTo("endDate", periodStartDate).GetSnapshotAsync(ct);

        var byId = new Dictionary<string, (DocumentReference, RosterSpot)>();
        foreach (var doc in open.Documents.Concat(closedDuring.Documents))
            byId[doc.Id] = (doc.Reference, doc.ConvertTo<RosterSpot>());
        return [.. byId.Values];
    }

    /// <summary>A team's currently-held spots.</summary>
    public static async Task<List<(DocumentReference Ref, RosterSpot Spot)>> OpenForTeamAsync(
        DocumentReference leagueDoc, string teamUsername, CancellationToken ct = default)
    {
        var snap = await Collection(leagueDoc)
            .WhereEqualTo("teamUsername", teamUsername)
            .WhereEqualTo("endDate", null)
            .GetSnapshotAsync(ct);
        return [.. snap.Documents.Select(d => (d.Reference, d.ConvertTo<RosterSpot>()))];
    }
}
