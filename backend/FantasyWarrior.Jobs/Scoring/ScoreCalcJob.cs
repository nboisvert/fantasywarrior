using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Scoring;

/// <summary>
/// Nightly score recalculation. For each league: fetch season raw totals of
/// every rostered player, run the top-X engine per team, add the adjustment
/// ledger, persist computed fields on team docs. Stateless and idempotent.
/// </summary>
public sealed class ScoreCalcJob(FirestoreDb db)
{
    public async Task RunAsync(string? onlyLeagueId = null, CancellationToken ct = default)
    {
        var leagues = onlyLeagueId is null
            ? (await db.Collection("leagues").GetSnapshotAsync(ct)).Documents.ToList()
            : [await db.Collection("leagues").Document(onlyLeagueId).GetSnapshotAsync(ct)];

        foreach (var leagueSnap in leagues.Where(l => l.Exists))
        {
            var league = leagueSnap.ConvertTo<League>();
            var teamsSnap = await leagueSnap.Reference.Collection("teams").GetSnapshotAsync(ct);
            var teams = teamsSnap.Documents.Select(d => (Doc: d, Team: d.ConvertTo<Team>())).ToList();

            var allPlayerIds = teams.SelectMany(t => t.Team.PlayerIds).Distinct().ToList();
            var totals = await PlayerTotalsSource.FetchAsync(db, allPlayerIds, league.Season, ct);
            var positions = await FetchPositionsAsync(allPlayerIds, ct);

            Console.WriteLine($"League '{league.Name}' [{leagueSnap.Id}] season {league.Season}: {teams.Count} teams, {allPlayerIds.Count} players");

            foreach (var (doc, team) in teams)
            {
                var entries = team.PlayerIds
                    .Select(id => new RosterEntry(
                        id,
                        positions.GetValueOrDefault(id, "C"),
                        totals.GetValueOrDefault(id) ?? new PlayerRawTotals()))
                    .ToList();
                var result = ScoringEngine.TeamScore(entries, league.RuleConfig);

                var adjustments = await doc.Reference.Collection("adjustments").GetSnapshotAsync(ct);
                var adjustmentsTotal = adjustments.Documents.Sum(a => a.GetValue<double>("delta"));

                await doc.Reference.UpdateAsync(new Dictionary<string, object>
                {
                    ["score"] = result.RawTopX + adjustmentsTotal,
                    ["rawTopXScore"] = result.RawTopX,
                    ["adjustmentsTotal"] = adjustmentsTotal,
                    ["countedPlayerIds"] = result.CountedPlayerIds.ToList(),
                    ["playerPoints"] = result.PlayerPoints.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                    ["scoreUpdatedUtc"] = Timestamp.GetCurrentTimestamp(),
                }, cancellationToken: ct);

                Console.WriteLine($"  {team.Name} (@{team.OwnerUsername}): score {result.RawTopX + adjustmentsTotal:0.##} (raw {result.RawTopX:0.##}, adj {adjustmentsTotal:0.##}), counted {result.CountedPlayerIds.Count}/{team.PlayerIds.Count}");
            }
        }
    }

    private async Task<Dictionary<long, string>> FetchPositionsAsync(IReadOnlyCollection<long> playerIds, CancellationToken ct)
    {
        if (playerIds.Count == 0)
            return [];
        var refs = playerIds.Select(id => db.Collection("players").Document(id.ToString())).ToArray();
        var snaps = await db.GetAllSnapshotsAsync(refs, ct);
        return snaps
            .Where(s => s.Exists)
            .ToDictionary(s => long.Parse(s.Id), s => s.GetValue<string>("position"));
    }
}
