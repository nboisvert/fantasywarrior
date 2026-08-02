using FantasyWarrior.Data;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Replays the season day by day, running each evening exactly as the nightly
/// pipeline would.
///
/// **Only the NHL fetch is skipped** — the game lines are already in the
/// database, so re-fetching them would be waste. Everything else really runs:
/// weekly scoring, banking, trade execution, lineup carry-forward. That is the
/// point: the weekly model had never been exercised over a real season, and a
/// replay is the only way to find out whether twenty-eight weeks of it hold up.
///
/// **It stops at every week end it crosses.** Jumping straight to a target date
/// would score every intervening week but execute every accepted trade at the
/// very end, which is not what would have happened. Trades take effect at the
/// week boundary they were accepted before, so the boundaries have to be walked.
///
/// Forward only. To replay a scenario, wipe and start over — a cursor that
/// could move backwards would need every banked week to be un-banked in step
/// with it, and getting that wrong is silent.
/// </summary>
public sealed class SimAdvanceJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(DateOnly target, bool dryRun, CancellationToken ct = default)
    {
        var clock = new SimulationClockService(db);
        var state = await clock.StateAsync(ct);
        if (state is null)
        {
            Console.Error.WriteLine("No simulation running. Start one with sql-sim-clock --set <date>.");
            return 1;
        }

        var from = state.AsOfDate;
        if (target <= from)
        {
            Console.Error.WriteLine($"Cursor is already at {from:yyyy-MM-dd}; the simulation only moves forward. "
                + "Wipe and reseed to replay.");
            return 1;
        }

        // Every week end strictly between here and the target, plus the target
        // itself. Those are the only days where anything different happens: the
        // rest of a week just rescores the same in-progress week.
        var stops = await db.Periods
            .Where(p => p.Season == state.Season && p.EndDate > from && p.EndDate < target)
            .OrderBy(p => p.EndDate)
            // The grace day: a week is banked the day after it ends, so that is
            // the evening worth stopping on.
            .Select(p => p.EndDate.AddDays(1))
            .ToListAsync(ct);
        stops = [.. stops.Where(d => d > from && d < target).Distinct().Order(), target];

        Console.WriteLine($"=== sim-advance  {from:yyyy-MM-dd} -> {target:yyyy-MM-dd}"
            + $"{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{stops.Count} stop(s): {string.Join(", ", stops.Select(d => d.ToString("MM-dd")))}\n");

        foreach (var stop in stops)
        {
            Console.WriteLine($"----- advancing to {stop:yyyy-MM-dd} -----");
            if (!dryRun) await clock.SetAsync(stop, state.Season, ct);
            await new NightlyJob(db).RunAsync(dryRun, null, SimulationClockService.FromAsOfDate(stop), ct);
            Console.WriteLine();
        }

        Console.WriteLine($"=== cursor now {target:yyyy-MM-dd} (simulated today: {target.AddDays(1):yyyy-MM-dd}) ===");
        return 0;
    }
}
