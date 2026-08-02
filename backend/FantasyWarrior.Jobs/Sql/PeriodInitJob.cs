using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using PeriodCalendar = FantasyWarrior.Core.Periods.PeriodCalendar;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Generates a season's weekly scoring calendar into <see cref="Period"/>.
///
/// Boundaries are derived from the games we already hold rather than fetched or
/// hardcoded — the schedule is data we own, and deriving it means the calendar
/// can never disagree with the games it scores.
///
/// **Append-only.** Existing weeks are never rewritten: points are banked per
/// period, so moving a boundary after the fact would silently restate history
/// that teams already own. Re-running only fills in weeks that do not exist yet.
/// </summary>
public sealed class PeriodInitJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(string season, bool dryRun, CancellationToken ct = default)
    {
        var days = await db.Games
            .Where(g => g.Season == season && g.GameType == GameType.RegularSeason)
            .GroupBy(g => g.GameDate)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        if (days.Count == 0)
        {
            Console.Error.WriteLine($"No regular-season games for {season}. Run stats-sync first.");
            return 1;
        }

        var perDay = days.ToDictionary(d => d.Date, d => d.Count);
        var first = days.Min(d => d.Date);
        var last = days.Max(d => d.Date);
        var spans = PeriodCalendar.Generate(first, last);

        Console.WriteLine($"=== period-init {season}{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{perDay.Values.Sum()} regular-season games over {days.Count} days, {first} -> {last}");
        Console.WriteLine($"{spans.Count} weeks, anchored on {spans[0].Start:yyyy-MM-dd} (Monday)\n");

        var existing = (await db.Periods
            .Where(p => p.Season == season)
            .Select(p => p.Number)
            .ToListAsync(ct)).ToHashSet();

        var now = DateTime.UtcNow;
        var created = 0;
        foreach (var span in spans)
        {
            var gameCount = Enumerable.Range(0, 7).Sum(i => perDay.GetValueOrDefault(span.Start.AddDays(i)));
            var known = existing.Contains(span.Index);

            Console.WriteLine($"  W{span.Index:00}  {span.Start:yyyy-MM-dd} -> {span.End:yyyy-MM-dd}  {gameCount,3} games"
                + (gameCount == 0 ? "   (break week)" : "")
                + (known ? "   [exists, untouched]" : ""));

            if (known || dryRun) continue;

            db.Periods.Add(new Period
            {
                Season = season,
                Number = span.Index,
                StartDate = span.Start,
                EndDate = span.End,
                LockUtc = PeriodCalendar.LockUtcFor(span.Start),
                GameCount = gameCount,
                CreatedUtc = now,
            });
            created++;
        }

        if (!dryRun) await db.SaveChangesAsync(ct);
        Console.WriteLine(dryRun
            ? $"\nDry run: nothing written ({spans.Count - existing.Count} would be created)."
            : $"\nCreated {created} week(s); {existing.Count} already existed and were left untouched.");
        return 0;
    }
}
