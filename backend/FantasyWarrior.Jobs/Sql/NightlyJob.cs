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
/// <item>execute accepted trades, effective the next week's start</item>
/// </list>
///
/// That order is load-bearing. Trades must run after banking, or a departing
/// player's banked points would be recomputed against a roster he already left.
/// It used to live only as step order in a YAML file, where nothing enforced it
/// and swapping two lines would silently corrupt a week.
///
/// The grace day exists because the NHL corrects boxscores after the fact.
/// Banking the evening a week ends would freeze whatever was known that night,
/// and a correction filed the next morning would be lost in silence.
///
/// There is no "materialize next week's lineups" step any more. The Firestore
/// model needed one because a lineup was a document that had to exist before a
/// GM could edit it; here a lineup is the set of IsActive flags on rows the
/// scoring pass creates anyway.
/// </summary>
public sealed class NightlyJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(
        bool dryRun, int? backfillFrom = null, DateTimeOffset? nowOverride = null,
        CancellationToken ct = default)
    {
        var now = nowOverride ?? await new SimulationClockSql(db).NowAsync(ct);
        var lastStatDate = PoolClock.LastStatDate(now);
        var simulated = nowOverride is not null || await new SimulationClockSql(db).StateAsync(ct) is not null;

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
                await new PeriodRollupJob(db).RunAsync(null, dryRun, now, number, ct);
                banked += await BankAsync(lastStatDate, dryRun, number, ct);
            }
            Console.WriteLine($"[backfill] {banked} week(s) banked.\n");
        }

        Console.WriteLine("[1/3] Scoring the current week");
        await new PeriodRollupJob(db).RunAsync(null, dryRun, now, null, ct);

        Console.WriteLine("\n[2/3] Banking finished weeks");
        banked += await BankAsync(lastStatDate, dryRun, null, ct);

        Console.WriteLine("\n[3/3] Executing accepted trades");
        if (banked == 0)
            Console.WriteLine("  No week closed tonight — trades wait for the next week end.");
        else if (dryRun)
            Console.WriteLine("  (skipped in dry run)");
        else
            await new ProcessTradesJob(db).RunAsync(await NextWeekStartAsync(lastStatDate, ct), ct);

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
    private async Task<int> BankAsync(DateOnly lastStatDate, bool dryRun, int? onlyNumber, CancellationToken ct)
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
            var affected = await db.RosterAssignments
                .Where(a => a.PeriodId == period.PeriodId && !a.IsFinalized)
                .CountAsync(ct);
            Console.WriteLine($"  W{period.Number:00} {period.Season} "
                + $"({period.StartDate:MM-dd}..{period.EndDate:MM-dd}) — {affected} assignment(s)");
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

    /// <summary>
    /// The start of the week now beginning — the date an executed trade takes
    /// effect from, so the incoming player is owned for that whole week rather
    /// than from part-way through it.
    /// </summary>
    private async Task<DateOnly> NextWeekStartAsync(DateOnly lastStatDate, CancellationToken ct) =>
        await db.Periods
            .Where(p => p.StartDate > lastStatDate)
            .OrderBy(p => p.StartDate)
            .Select(p => (DateOnly?)p.StartDate)
            .FirstOrDefaultAsync(ct)
        // Past the last week of the season: nothing further starts, so the day
        // after the last results is as close as it gets.
        ?? lastStatDate.AddDays(1);
}
