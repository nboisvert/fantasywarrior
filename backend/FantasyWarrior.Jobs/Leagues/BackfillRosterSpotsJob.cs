using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Leagues;

/// <summary>
/// Copies existing <c>assignments</c> into the new <c>rosterSpots</c>
/// collection, so leagues created before the dual-write have the same history
/// as ones created after it.
///
/// Deterministic document ids (the source assignment's id) make this
/// idempotent: re-running overwrites the same documents instead of creating
/// duplicates. Rollup fields are left at zero — the scoring job owns those and
/// recomputes them from the weekly lineups.
///
/// A league can also be seeded without any assignments at all (seed-mordus
/// writes team.playerIds directly), so --from-rosters opens one spot per
/// rostered player instead, dated from the league's creation.
/// </summary>
public sealed class BackfillRosterSpotsJob(FirestoreDb db)
{
    public async Task<int> RunAsync(string? onlyLeagueId, bool fromRosters, bool dryRun, CancellationToken ct = default)
    {
        var leagues = onlyLeagueId is null
            ? (await db.Collection("leagues").GetSnapshotAsync(ct)).Documents.ToList()
            : [await db.Collection("leagues").Document(onlyLeagueId).GetSnapshotAsync(ct)];

        foreach (var leagueSnap in leagues.Where(l => l.Exists))
        {
            var league = leagueSnap.ConvertTo<League>();
            var existing = (await RosterSpots.Collection(leagueSnap.Reference).GetSnapshotAsync(ct)).Count;
            Console.WriteLine($"\n=== {league.Name} [{leagueSnap.Id}] — {existing} existing spot(s) ===");

            var written = fromRosters
                ? await FromRostersAsync(leagueSnap, league, dryRun, ct)
                : await FromAssignmentsAsync(leagueSnap, dryRun, ct);

            Console.WriteLine(dryRun ? $"  Dry run: {written} spot(s) would be written." : $"  Wrote {written} spot(s).");
        }
        return 0;
    }

    private async Task<int> FromAssignmentsAsync(DocumentSnapshot leagueSnap, bool dryRun, CancellationToken ct)
    {
        var assignments = await leagueSnap.Reference.Collection("assignments").GetSnapshotAsync(ct);
        if (assignments.Count == 0)
        {
            Console.WriteLine("  No assignments — pass --from-rosters to seed spots from team.playerIds instead.");
            return 0;
        }

        var positions = await PositionsAsync(
            assignments.Documents.Select(d => d.ConvertTo<Assignment>().PlayerId).Distinct().ToList(), ct);

        var written = 0;
        foreach (var doc in assignments.Documents)
        {
            var a = doc.ConvertTo<Assignment>();
            var position = positions.GetValueOrDefault(a.PlayerId, "C");
            var spot = new RosterSpot
            {
                PlayerId = a.PlayerId,
                TeamUsername = a.TeamUsername,
                Position = position,
                PositionGroup = PositionGroups.CodeFrom(position),
                StartDate = a.From,
                StartReason = a.CreationEvent,
                StartRefId = a.CreationEventReferenceId,
                EndDate = a.To,
                EndReason = a.CloseReason,
                EndRefId = a.CloseReasonReferenceId,
                OpenedUtc = a.CreatedUtc,
                ClosedUtc = a.ClosedUtc,
            };
            if (!dryRun)
                await RosterSpots.Collection(leagueSnap.Reference).Document(doc.Id).SetAsync(spot, cancellationToken: ct);
            written++;
        }
        Console.WriteLine($"  From {assignments.Count} assignment(s).");
        return written;
    }

    private async Task<int> FromRostersAsync(DocumentSnapshot leagueSnap, League league, bool dryRun, CancellationToken ct)
    {
        var teams = await leagueSnap.Reference.Collection("teams").GetSnapshotAsync(ct);
        var allIds = teams.Documents.SelectMany(d => d.ConvertTo<Team>().PlayerIds).Distinct().ToList();
        var positions = await PositionsAsync(allIds, ct);
        var startDate = league.CreatedUtc.ToDateTime().ToString("yyyy-MM-dd");

        var written = 0;
        foreach (var teamDoc in teams.Documents)
        {
            var team = teamDoc.ConvertTo<Team>();
            foreach (var playerId in team.PlayerIds)
            {
                var position = positions.GetValueOrDefault(playerId, "C");
                // Deterministic id so a re-run overwrites rather than duplicates.
                var id = $"{team.OwnerUsername}_{playerId}";
                if (!dryRun)
                    await RosterSpots.Collection(leagueSnap.Reference).Document(id).SetAsync(new RosterSpot
                    {
                        PlayerId = playerId,
                        TeamUsername = team.OwnerUsername,
                        Position = position,
                        PositionGroup = PositionGroups.CodeFrom(position),
                        StartDate = startDate,
                        StartReason = RosterSpotReason.Draft,
                        OpenedUtc = league.CreatedUtc,
                    }, cancellationToken: ct);
                written++;
            }
            Console.WriteLine($"    {team.OwnerUsername,-12} {team.PlayerIds.Count} spot(s)");
        }
        return written;
    }

    private async Task<Dictionary<long, string>> PositionsAsync(List<long> ids, CancellationToken ct)
    {
        var positions = new Dictionary<long, string>();
        foreach (var chunk in ids.Chunk(300))
        {
            var refs = chunk.Select(id => db.Collection("players").Document(id.ToString())).ToArray();
            foreach (var snap in await db.GetAllSnapshotsAsync(refs, ct))
                if (snap.Exists)
                    positions[long.Parse(snap.Id)] = snap.ConvertTo<FantasyWarrior.Core.Players.Player>().Position;
        }
        return positions;
    }
}
