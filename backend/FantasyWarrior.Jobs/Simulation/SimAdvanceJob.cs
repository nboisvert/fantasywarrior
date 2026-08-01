using FantasyWarrior.Core.Periods;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Jobs.Nightly;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Simulation;

/// <summary>
/// Moves the simulated season forward to a target day, processing each evening
/// the way the nightly pipeline would have.
///
/// The one step deliberately skipped is fetching boxscores from the NHL API:
/// the 2025-26 season is already in Firestore. Everything else — player
/// aggregates, weekly scoring, banking, trades, next week's lineups — runs for
/// real.
///
/// **It stops at every period boundary**, and that is the whole point. Jumping
/// straight to the target would execute a trade proposed in week 5 at week 8's
/// boundary instead of week 5's. Stopping means each week is scored, banked and
/// its trades applied before the next begins, exactly as a real season would.
/// </summary>
public sealed class SimAdvanceJob(FirestoreDb db)
{
    /// <summary>Past this, a single advance is eating a serious share of the daily free-tier budget.</summary>
    private const int ReadWarningThreshold = 20_000;

    public async Task<int> RunAsync(string targetDate, bool dryRun, CancellationToken ct = default)
    {
        var clock = new SimulationClock(db);
        var state = await clock.StateAsync(ct);
        if (state is null)
        {
            Console.Error.WriteLine("No simulation running. Start one with `sim-reset --season <s>`.");
            return 1;
        }

        if (!DateOnly.TryParse(targetDate, out var target))
        {
            Console.Error.WriteLine("--to expects YYYY-MM-DD.");
            return 1;
        }

        var current = DateOnly.Parse(state.AsOfDate);
        if (target <= current)
        {
            Console.Error.WriteLine(
                $"Already at {state.AsOfDate}; the simulation only moves forward. "
                + "To replay from earlier, run `sim-reset`.");
            return 1;
        }

        var periods = (await db.Collection("periods")
                .WhereEqualTo("season", state.Season).OrderBy("index").GetSnapshotAsync(ct))
            .Documents.Select(d => d.ConvertTo<Period>()).ToList();
        if (periods.Count == 0)
        {
            Console.Error.WriteLine($"No period calendar for season {state.Season}.");
            return 1;
        }

        var seasonEnd = DateOnly.Parse(periods[^1].EndDate);
        if (target > seasonEnd)
        {
            Console.Error.WriteLine($"{targetDate} is past the end of the season ({seasonEnd:yyyy-MM-dd}).");
            return 1;
        }

        // Every week-end strictly between where we are and where we're going,
        // plus the target itself. Each becomes one nightly run.
        var stops = periods
            .Select(p => DateOnly.Parse(p.EndDate))
            .Where(end => end > current && end < target)
            .Append(target)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        Console.WriteLine($"=== sim-advance {state.AsOfDate} -> {targetDate}{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{(target.DayNumber - current.DayNumber)} day(s), {stops.Count} stop(s): "
            + string.Join(", ", stops.Select(s => s.ToString("yyyy-MM-dd"))));
        if (stops.Count > 1)
            Console.WriteLine("Stopping at each week end so that week's trades execute before the next begins.\n");
        else
            Console.WriteLine();

        await WarnAboutPendingTradesAsync(stops.Count, ct);

        var totalReads = 0;
        foreach (var stop in stops)
        {
            var iso = PoolClock.Iso(stop);
            var period = periods.FirstOrDefault(p =>
                string.CompareOrdinal(p.StartDate, iso) <= 0 && string.CompareOrdinal(iso, p.EndDate) <= 0);
            Console.WriteLine($"----- {iso}  (week {period?.Index.ToString("00") ?? "--"}) -----");

            var (_, reads, _) = await new AdvanceSeasonStatsJob(db).RunAsync(state.Season, iso, dryRun, ct);
            totalReads += reads;

            // The nightly job's own clock read would still see the old cursor,
            // so the instant is handed to it explicitly.
            await new NightlyJob(db).RunAsync(
                commitScore: true, dryRun: dryRun, backfillFrom: null,
                nowOverride: SimulationClock.FromAsOfDate(stop), ct: ct);

            if (!dryRun) await clock.SetAsync(iso, state.Season, ct);
            Console.WriteLine();
        }

        Console.WriteLine($"=== now at {targetDate} ===");
        if (totalReads > ReadWarningThreshold)
            Console.Error.WriteLine(
                $"WARNING: ~{totalReads:N0} reads for this advance. The free tier allows 50,000/day — "
                + "advance a week at a time rather than a month.");
        return 0;
    }

    /// <summary>
    /// An accepted trade executes at the next boundary crossed. Advancing past
    /// several at once is legal but means it lands at the first of them, which
    /// is worth saying out loud rather than discovering afterwards.
    /// </summary>
    private async Task WarnAboutPendingTradesAsync(int stopCount, CancellationToken ct)
    {
        if (stopCount <= 1) return;

        var leagues = await db.Collection("leagues").GetSnapshotAsync(ct);
        var accepted = 0;
        foreach (var leagueDoc in leagues.Documents)
            accepted += (await leagueDoc.Reference.Collection("trades")
                .WhereEqualTo("status", "accepted").GetSnapshotAsync(ct)).Count;

        if (accepted > 0)
            Console.WriteLine($"NOTE: {accepted} accepted trade(s) will execute at the FIRST boundary crossed.\n");
    }
}
