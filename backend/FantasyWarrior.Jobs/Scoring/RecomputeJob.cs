using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Lineups;
using FantasyWarrior.Core.Periods;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Scoring;

/// <summary>
/// Un-banks weeks so they can be scored again.
///
/// Banked points are deliberately immutable — that is what makes trades free
/// of retroactive effects — so this is the only way back, and it needs to
/// exist for three real cases: a scoring bug found after the fact, a
/// commissioner changing the rules mid-season, and a week banked at zero
/// because its lineups had not been generated yet.
///
/// It resets the finalization guards and recomputes each team's banked total
/// from the lineups of the weeks *before* the cut, which is cheap: lineup
/// documents only, no game lines. Re-scoring the un-banked weeks is a separate
/// step (`nightly --backfill-from N`), kept separate because that one is
/// expensive.
/// </summary>
public sealed class RecomputeJob(FirestoreDb db)
{
    public async Task<int> RunAsync(string season, int fromPeriod, bool dryRun, CancellationToken ct = default)
    {
        if (fromPeriod < 1)
        {
            Console.Error.WriteLine("--from must be 1 or greater.");
            return 1;
        }

        var periods = (await db.Collection("periods").WhereEqualTo("season", season).GetSnapshotAsync(ct))
            .Documents.Select(d => (d.Reference, Period: d.ConvertTo<Period>()))
            .OrderBy(p => p.Period.Index).ToList();
        if (periods.Count == 0)
        {
            Console.Error.WriteLine($"No period calendar for season {season}.");
            return 1;
        }

        var toClear = periods.Where(p => p.Period.Index >= fromPeriod && p.Period.FinalizedUtc is not null).ToList();
        Console.WriteLine($"=== recompute {season} from week {fromPeriod}{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{toClear.Count} finalized week(s) to un-bank (weeks 1..{fromPeriod - 1} stay banked).\n");

        var leagues = (await db.Collection("leagues").WhereEqualTo("season", season).GetSnapshotAsync(ct))
            .Documents.Select(d => (d.Reference, League: d.ConvertTo<League>())).ToList();

        foreach (var (leagueRef, league) in leagues)
        {
            Console.WriteLine($"--- {league.Name} ---");
            var teams = await leagueRef.Collection("teams").GetSnapshotAsync(ct);

            foreach (var teamDoc in teams.Documents)
            {
                var team = teamDoc.ConvertTo<Team>();

                // Rebuild the kept total from the weeks before the cut, rather
                // than trusting the stored one -- if it were already wrong,
                // subtracting from it would carry the error forward.
                double kept = 0;
                for (var i = 1; i < fromPeriod; i++)
                {
                    var snap = await leagueRef.Collection("lineups")
                        .Document(Lineup.DocId(team.OwnerUsername, i)).GetSnapshotAsync(ct);
                    if (snap.Exists) kept += snap.ConvertTo<Lineup>().ActivePoints;
                }

                Console.WriteLine($"  {team.OwnerUsername,-12} banked {team.FinalizedScore,7:0.#} "
                    + $"(through W{team.FinalizedThroughPeriodIndex:00})  ->  {kept,7:0.#} (through W{fromPeriod - 1:00})");
                if (dryRun) continue;

                await teamDoc.Reference.UpdateAsync(new Dictionary<string, object>
                {
                    ["finalizedScore"] = kept,
                    ["finalizedThroughPeriodIndex"] = fromPeriod - 1,
                }, cancellationToken: ct);
            }

            // Roster spots carry the same guard.
            var spots = await RosterSpots.Collection(leagueRef).GetSnapshotAsync(ct);
            if (!dryRun)
                foreach (var chunk in spots.Documents.Chunk(450))
                {
                    var batch = db.StartBatch();
                    foreach (var doc in chunk)
                        batch.Update(doc.Reference, new Dictionary<string, object>
                        {
                            ["finalizedActivePoints"] = 0.0,
                            ["finalizedBenchPoints"] = 0.0,
                            ["finalizedThroughPeriodIndex"] = 0,
                        });
                    await batch.CommitAsync(ct);
                }
            Console.WriteLine($"  {spots.Count} roster spot guard(s) reset.");
        }

        if (!dryRun)
            foreach (var (periodRef, _) in toClear)
                await periodRef.UpdateAsync("finalizedUtc", null, cancellationToken: ct);

        Console.WriteLine(dryRun
            ? "\nDry run: nothing written."
            : $"\nUn-banked {toClear.Count} week(s). Re-score them with: nightly --backfill-from {fromPeriod}");
        return 0;
    }
}
