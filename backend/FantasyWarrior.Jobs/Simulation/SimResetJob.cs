using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Periods;
using FantasyWarrior.Core.Time;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Simulation;

/// <summary>
/// Rewinds a season to its eve so it can be replayed day by day.
///
/// Keeps what makes the league a league — who owns which player, the teams,
/// the users, the rules — and clears only what a replay is meant to produce:
/// scores, weekly lineups, banked points and player aggregates.
///
/// The cursor lands the day *before* the first period starts, which is what
/// makes the opening week's lineup editable. Starting it on the Monday would
/// lock week 1 before anyone had a chance to set it.
/// </summary>
public sealed class SimResetJob(FirestoreDb db)
{
    private const int BatchLimit = 450;

    public async Task<int> RunAsync(string season, bool dryRun, CancellationToken ct = default)
    {
        var periods = (await db.Collection("periods")
                .WhereEqualTo("season", season).OrderBy("index").GetSnapshotAsync(ct))
            .Documents.Select(d => (d.Reference, Period: d.ConvertTo<Period>())).ToList();
        if (periods.Count == 0)
        {
            Console.Error.WriteLine($"No period calendar for season {season} — run period-init first.");
            return 1;
        }

        var first = periods[0].Period;
        // The simulated day must be the eve, not the Monday: week 1's lock
        // falls at midnight on its start date, so landing on the start date
        // itself would lock the opening lineup before anyone could set it.
        // The cursor is "last day with results", which is one behind the
        // simulated day — hence two days before the season opens.
        var simulatedDay = DateOnly.Parse(first.StartDate).AddDays(-1);
        var cursor = simulatedDay.AddDays(-1);

        Console.WriteLine($"=== sim-reset {season}{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{periods.Count} weeks, first starts {first.StartDate}.");
        Console.WriteLine($"Simulated day will be {simulatedDay:yyyy-MM-dd} (the eve) — week 1 open and editable.");
        Console.WriteLine($"Cursor (last day with results): {cursor:yyyy-MM-dd}.\n");

        var leagues = (await db.Collection("leagues").WhereEqualTo("season", season).GetSnapshotAsync(ct))
            .Documents.Select(d => (d.Reference, League: d.ConvertTo<League>())).ToList();
        if (leagues.Count == 0)
        {
            Console.Error.WriteLine($"No league found for season {season}.");
            return 1;
        }

        foreach (var (leagueRef, league) in leagues)
        {
            Console.WriteLine($"--- {league.Name} [{leagueRef.Id}] ---");

            var teams = await leagueRef.Collection("teams").GetSnapshotAsync(ct);
            Console.WriteLine($"  {teams.Count} team(s): scores, banked totals and weekly history reset.");
            if (!dryRun)
                foreach (var teamDoc in teams.Documents)
                    await teamDoc.Reference.UpdateAsync(new Dictionary<string, object>
                    {
                        ["score"] = 0.0,
                        ["finalizedScore"] = 0.0,
                        ["finalizedThroughPeriodIndex"] = 0,
                        ["periodPoints"] = 0.0,
                        ["benchScore"] = 0.0,
                        ["currentPeriodIndex"] = 0,
                        ["periodScores"] = new Dictionary<string, double>(),
                        ["playerPoints"] = new Dictionary<string, double>(),
                    }, cancellationToken: ct);

            // Roster spots survive — who owns whom is the one thing a replay
            // must not invent. Only their accumulated numbers go.
            var spots = await RosterSpots.Collection(leagueRef).GetSnapshotAsync(ct);
            Console.WriteLine($"  {spots.Count} roster spot(s) KEPT; rollups and guards zeroed.");
            if (!dryRun)
                foreach (var chunk in spots.Documents.Chunk(BatchLimit))
                {
                    var batch = db.StartBatch();
                    foreach (var doc in chunk)
                        batch.Update(doc.Reference, new Dictionary<string, object>
                        {
                            ["finalizedThroughPeriodIndex"] = 0,
                            ["finalizedActivePoints"] = 0.0,
                            ["finalizedBenchPoints"] = 0.0,
                            ["activePoints"] = 0.0,
                            ["benchPoints"] = 0.0,
                            ["activeGamesPlayed"] = 0,
                        });
                    await batch.CommitAsync(ct);
                }

            var lineups = await leagueRef.Collection("lineups").GetSnapshotAsync(ct);
            Console.WriteLine($"  {lineups.Count} lineup(s) deleted.");
            if (!dryRun) await DeleteAllAsync(lineups, ct);

            // A clean replay shouldn't inherit demo trades from another run.
            var trades = await leagueRef.Collection("trades").GetSnapshotAsync(ct);
            Console.WriteLine($"  {trades.Count} trade(s) deleted.");
            if (!dryRun)
                foreach (var tradeDoc in trades.Documents)
                {
                    var votes = await tradeDoc.Reference.Collection("votes").GetSnapshotAsync(ct);
                    await DeleteAllAsync(votes, ct);
                    await tradeDoc.Reference.DeleteAsync(cancellationToken: ct);
                }
        }

        var finalized = periods.Count(p => p.Period.FinalizedUtc is not null);
        Console.WriteLine($"\n{finalized} finalized week(s) un-banked.");
        if (!dryRun)
            foreach (var (periodRef, period) in periods.Where(p => p.Period.FinalizedUtc is not null))
                await periodRef.UpdateAsync("finalizedUtc", null, cancellationToken: ct);

        var stats = await db.Collection("playerSeasonStats").WhereEqualTo("season", season).GetSnapshotAsync(ct);
        Console.WriteLine($"{stats.Count} player aggregate(s) deleted — they rebuild as the replay advances.");
        if (!dryRun) await DeleteAllAsync(stats, ct);

        if (dryRun)
        {
            Console.WriteLine("\nDry run: nothing written.");
            return 0;
        }

        await new SimulationClock(db).SetAsync(PoolClock.Iso(cursor), season, ct);
        Console.WriteLine($"\nThe app now believes it is {simulatedDay:yyyy-MM-dd}, the day before the season opens.");
        Console.WriteLine("Next: set week 1 lineups in the app, then `sim-advance --to <date>`.");
        return 0;
    }

    private async Task DeleteAllAsync(QuerySnapshot snap, CancellationToken ct)
    {
        foreach (var chunk in snap.Documents.Chunk(BatchLimit))
        {
            var batch = db.StartBatch();
            foreach (var doc in chunk) batch.Delete(doc.Reference);
            await batch.CommitAsync(ct);
        }
    }
}
