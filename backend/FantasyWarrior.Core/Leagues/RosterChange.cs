using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// Applies a roster change to one team: closes the outgoing players' roster
/// spots, opens spots for the incoming ones, and updates the team's player
/// list. Generalizes single-player add/drop to N-out/M-in so a two-sided trade
/// calls it once per team.
///
/// This used to carry the transaction-invariance machinery — compute the team's
/// top-X before and after, then write a compensating ledger entry so the score
/// wouldn't jump at transaction time. Weekly banked points make all of that
/// unnecessary: a week's points belong to whoever fielded the player that week,
/// permanently, so a trade cannot move history and there is nothing to
/// compensate for. The ledger, the before/after scoring and the score writes
/// are gone; what remains is bookkeeping.
///
/// Lives in Core so the API's add/drop endpoints and the Jobs project's trade
/// processing share one path.
/// </summary>
public static class RosterChange
{
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
        string startReason,
        string? startRefId,
        string endReason,
        string? endRefId,
        string effectiveDate,
        CancellationToken ct = default)
    {
        var now = Timestamp.GetCurrentTimestamp();

        foreach (var playerId in playersOut)
        {
            var open = await RosterSpots.Collection(leagueDoc)
                .WhereEqualTo("playerId", playerId)
                .WhereEqualTo("teamUsername", team.OwnerUsername)
                .WhereEqualTo("endDate", null)
                .GetSnapshotAsync(ct);
            var fields = RosterSpots.BuildClosedFields(effectiveDate, now, endReason, endRefId);
            foreach (var doc in open.Documents)
                await doc.Reference.UpdateAsync(fields, cancellationToken: ct);
        }

        foreach (var spot in RosterSpots.BuildOpened(
            playersIn, team.OwnerUsername, positions, startReason, startRefId, effectiveDate, now))
            await RosterSpots.Collection(leagueDoc).AddAsync(spot, ct);

        // playerIds is a denormalization of "open spots", kept because the
        // one-owner-per-league check and league-detail both read it directly.
        await teamDoc.UpdateAsync(
            new Dictionary<string, object> { ["playerIds"] = BuildNewPlayerIds(team.PlayerIds, playersOut, playersIn) },
            cancellationToken: ct);
    }
}
