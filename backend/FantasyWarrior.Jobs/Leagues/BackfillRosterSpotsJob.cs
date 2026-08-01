using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Leagues;

/// <summary>
/// Opens one <see cref="RosterSpot"/> per player in each team's roster, for
/// leagues seeded straight into <c>team.playerIds</c> (seed-mordus) rather than
/// through the roster endpoints.
///
/// Spots start at the season's first period, not the league's creation date:
/// a league imported from a past season would otherwise have every scoring
/// window fall before its spots existed, and every team would score zero.
///
/// Deterministic document ids make it idempotent — re-running overwrites the
/// same documents rather than duplicating them. Rollup fields are left at zero;
/// the scoring job owns those and derives them from the weekly lineups.
/// </summary>
public sealed class BackfillRosterSpotsJob(FirestoreDb db)
{
    public async Task<int> RunAsync(
        string? onlyLeagueId, string? startDateOverride, bool dryRun, CancellationToken ct = default)
    {
        var leagues = onlyLeagueId is null
            ? (await db.Collection("leagues").GetSnapshotAsync(ct)).Documents.ToList()
            : [await db.Collection("leagues").Document(onlyLeagueId).GetSnapshotAsync(ct)];

        foreach (var leagueSnap in leagues.Where(l => l.Exists))
        {
            var league = leagueSnap.ConvertTo<League>();
            var existing = (await RosterSpots.Collection(leagueSnap.Reference).GetSnapshotAsync(ct)).Count;
            Console.WriteLine($"\n=== {league.Name} [{leagueSnap.Id}] — {existing} existing spot(s) ===");

            var written = await FromRostersAsync(leagueSnap, league, startDateOverride, dryRun, ct);

            Console.WriteLine(dryRun ? $"  Dry run: {written} spot(s) would be written." : $"  Wrote {written} spot(s).");
        }
        return 0;
    }

    private async Task<int> FromRostersAsync(
        DocumentSnapshot leagueSnap, League league, string? startDateOverride, bool dryRun, CancellationToken ct)
    {
        var teams = await leagueSnap.Reference.Collection("teams").GetSnapshotAsync(ct);
        var allIds = teams.Documents.SelectMany(d => d.ConvertTo<Team>().PlayerIds).Distinct().ToList();
        var positions = await PositionsAsync(allIds, ct);
        var startDate = startDateOverride ?? await SeasonStartAsync(league.Season, ct);
        Console.WriteLine($"  Spots start {startDate}.");

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

    /// <summary>
    /// The season's first period start, so a league seeded from a past
    /// season's rosters owns its players for that whole season.
    ///
    /// Dating spots from the league's creation instead would be silently wrong
    /// for exactly this case: every scoring window would fall entirely before
    /// the spots existed, and every team would score zero forever.
    /// </summary>
    private async Task<string> SeasonStartAsync(string season, CancellationToken ct)
    {
        var snap = await db.Collection("periods")
            .WhereEqualTo("season", season).OrderBy("index").Limit(1).GetSnapshotAsync(ct);
        return snap.Documents.FirstOrDefault()?.ConvertTo<FantasyWarrior.Core.Periods.Period>().StartDate
            ?? throw new InvalidOperationException(
                $"No period calendar for season {season} — run period-init first, or pass --start-date.");
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
