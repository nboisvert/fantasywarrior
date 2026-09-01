using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// The one nightly entry point, in the one order that is correct:
///
/// <list type="number">
/// <item>score the current week</item>
/// <item>bank any week whose grace day has passed</item>
/// <item>prepare the week ahead: carry its lineups forward, land due trades</item>
/// <item>snapshot standings for the rank-movement pill</item>
/// </list>
///
/// That order is load-bearing, though less delicately than it once was. Step 3
/// used to execute trades and had to follow banking, or a departing player's
/// banked points would be recomputed against a roster he had already left.
/// Trades are applied at acceptance now, dated forward, so what is left is the
/// weaker requirement that next week is carried forward from a week that has
/// been scored.
///
/// The grace day exists because the NHL corrects boxscores after the fact.
/// Banking the evening a week ends would freeze whatever was known that night,
/// and a correction filed the next morning would be lost in silence.
///
/// <b>Step 3 runs every night</b>, not only on the nights a week closes. It is
/// idempotent — it fills in what is missing and rewrites nothing — and a GM who
/// wants to set next week's lineup should not have to wait for a week to end
/// before the rows exist.
/// </summary>
public sealed class NightlyJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(
        bool dryRun, int? backfillFrom = null, DateTimeOffset? nowOverride = null,
        CancellationToken ct = default)
    {
        var now = nowOverride ?? await new SimulationClockService(db).NowAsync(ct);
        var lastStatDate = PoolClock.LastStatDate(now);
        var simulated = nowOverride is not null || await new SimulationClockService(db).StateAsync(ct) is not null;

        Console.WriteLine($"===== nightly {PoolClock.TodayEt(now):yyyy-MM-dd} (ET)"
            + $"{(simulated ? "  [SIMULATED]" : "")}{(dryRun ? "  [DRY RUN]" : "")} =====\n");

        var banked = 0;
        if (backfillFrom is not null)
        {
            // Replaying history has to alternate score-then-bank week by week:
            // a week is only frozen at banking, so scoring several in a row
            // would leave only the last one unfrozen and the rest unscored.
            Console.WriteLine($"[backfill] Replaying from week {backfillFrom}.\n");
            foreach (var number in await PendingWeeksAsync(backfillFrom.Value, lastStatDate, ct))
            {
                Console.WriteLine($"--- week {number} ---");
                banked += await BankAsync(lastStatDate, dryRun, number, now, ct);
            }
            Console.WriteLine($"[backfill] {banked} week(s) banked.\n");
        }

        Console.WriteLine("[1/4] Scoring the current week");
        await new PeriodRollupJob(db).RunAsync(null, dryRun, now, null, ct);

        Console.WriteLine("\n[2/4] Banking finished weeks");
        banked += await BankAsync(lastStatDate, dryRun, null, now, ct);

        Console.WriteLine("\n[3/4] Preparing the week ahead");
        if (dryRun)
            Console.WriteLine("  (skipped in dry run)");
        else
            await new WeekAheadJob(db).RunAsync(PoolClock.TodayEt(now), ct);

        Console.WriteLine("\n[4/4] Snapshotting standings");
        if (dryRun)
            Console.WriteLine("  (skipped in dry run)");
        else
            await new StandingsSnapshotJob(db).RunAsync(null, dryRun, now, ct);

        Console.WriteLine($"\n===== nightly done ({banked} week(s) banked) =====");
        return 0;
    }

    /// <summary>Weeks ready to bank, oldest first.</summary>
    private async Task<List<int>> PendingWeeksAsync(int from, DateOnly lastStatDate, CancellationToken ct) =>
        (await db.Periods
            .Where(p => p.Number >= from && p.FinalizedUtc == null)
            .OrderBy(p => p.Number)
            .ToListAsync(ct))
        .Where(p => PeriodScoring.ShouldFinalize(p.EndDate, lastStatDate))
        .Select(p => p.Number)
        .Distinct()
        .ToList();

    /// <summary>
    /// Freezes every assignment of a week whose grace day has passed, then
    /// stamps the week itself — in that order, so a crash between the two
    /// leaves the week unbanked and the next run simply redoes it.
    ///
    /// Freezing is the whole of banking here. There is no running total to
    /// increment, so the double-counting guard the Firestore model needed
    /// (finalizedThroughPeriodIndex, written in the same atomic update as the
    /// value it protected) has nothing to protect: setting a flag twice is
    /// setting a flag.
    /// </summary>
    private async Task<int> BankAsync(
        DateOnly lastStatDate, bool dryRun, int? onlyNumber, DateTimeOffset now, CancellationToken ct)
    {
        var pending = (await db.Periods
            .Where(p => p.FinalizedUtc == null && (onlyNumber == null || p.Number == onlyNumber))
            .OrderBy(p => p.Season).ThenBy(p => p.Number)
            .ToListAsync(ct))
            .Where(p => PeriodScoring.ShouldFinalize(p.EndDate, lastStatDate))
            .ToList();

        if (pending.Count == 0)
        {
            Console.WriteLine("  Nothing ready to bank.");
            return 0;
        }

        var done = 0;
        foreach (var period in pending)
        {
            Console.WriteLine($"  W{period.Number:00} {period.Season} "
                + $"({period.StartDate:MM-dd}..{period.EndDate:MM-dd})");

            // **Score it one last time, then freeze.** Step 1 only scores the
            // week in progress, which by the time a week is bankable is already
            // the *next* one — so without this, a week would be frozen holding
            // whatever partial numbers it had when it stopped being current.
            // In a replay it would be frozen at zero.
            //
            // Idempotent, and the reason the grace day exists: this final pass
            // is what picks up boxscore corrections the NHL filed after the week
            // ended.
            await new PeriodRollupJob(db).RunAsync(null, dryRun, now, period.Number, ct);

            var affected = await db.RosterAssignments
                .Where(a => a.PeriodId == period.PeriodId && !a.IsFinalized)
                .CountAsync(ct);
            Console.WriteLine($"    freezing {affected} assignment(s)");
            if (dryRun) continue;

            await db.RosterAssignments
                .Where(a => a.PeriodId == period.PeriodId && !a.IsFinalized)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsFinalized, true), ct);

            period.FinalizedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            done++;
        }
        return done;
    }
}
