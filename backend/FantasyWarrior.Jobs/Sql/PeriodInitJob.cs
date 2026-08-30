using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Seasons;
using Microsoft.EntityFrameworkCore;
using PeriodCalendar = FantasyWarrior.Core.Periods.PeriodCalendar;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Generates a season's weekly scoring calendar into <see cref="Period"/>.
///
/// Boundaries come from two sources reconciled by <see cref="SeasonBounds"/>:
/// the dates the NHL <b>declared</b> (the <c>Seasons</c> row) and the games we
/// have actually <b>observed</b>. Either alone is not enough — declared dates
/// are what let next season's calendar exist before a single game is imported,
/// and the games are what keep a rescheduled date from falling outside every
/// week and scoring for nobody.
///
/// **Boundaries are append-only.** Existing weeks are never moved: points are
/// banked per period, so shifting one after the fact would silently restate
/// history that teams already own.
///
/// **<see cref="Period.GameCount"/> is the one exception, and deliberately so.**
/// A season built from declared dates has no games yet, so every week would read
/// as a break week forever. Nothing is banked against a count — it only lets the
/// UI say "pause" — so re-running refreshes it on weeks that are not finalized.
/// Run it after `stats-sync` and the counts catch up on their own.
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

        var declared = await SeasonLookup.DeclaredWindowAsync(db, season, ct);
        var observed = days.Count == 0
            ? (SeasonWindow?)null
            : new SeasonWindow(days.Min(d => d.Date), days.Max(d => d.Date));

        if (SeasonBounds.Resolve(declared, observed) is not { } bounds)
        {
            Console.Error.WriteLine(
                $"Nothing known about {season}: no regular-season games imported and no Seasons row. "
                + $"Run `season-init --season {season} --start <date> --end <date>`, or `stats-sync` first.");
            return 1;
        }

        var perDay = days.ToDictionary(d => d.Date, d => d.Count);
        var spans = PeriodCalendar.Generate(bounds.Start, bounds.End);

        Console.WriteLine($"=== period-init {season}{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine(declared is { } d
            ? $"Declared {d.Start:yyyy-MM-dd} -> {d.End:yyyy-MM-dd}"
            : "Declared: no Seasons row");
        Console.WriteLine(observed is { } o
            ? $"Observed {perDay.Values.Sum()} regular-season games over {days.Count} days, {o.Start:yyyy-MM-dd} -> {o.End:yyyy-MM-dd}"
            : "Observed: no games imported");
        Console.WriteLine($"{spans.Count} weeks over {bounds.Start:yyyy-MM-dd} -> {bounds.End:yyyy-MM-dd}, "
            + $"anchored on {spans[0].Start:yyyy-MM-dd} (Monday)\n");

        var existing = await db.Periods
            .Where(p => p.Season == season)
            .ToDictionaryAsync(p => p.Number, ct);

        var now = DateTime.UtcNow;
        int created = 0, recounted = 0;
        foreach (var span in spans)
        {
            var gameCount = Enumerable.Range(0, 7).Sum(i => perDay.GetValueOrDefault(span.Start.AddDays(i)));
            existing.TryGetValue(span.Index, out var known);

            // A finalized week is frozen whole: its count described the games
            // its points were banked from, and restating it would describe a
            // week that is over with numbers nobody scored under.
            var stale = known is { FinalizedUtc: null } && known.GameCount != gameCount;

            Console.WriteLine($"  W{span.Index:00}  {span.Start:yyyy-MM-dd} -> {span.End:yyyy-MM-dd}  {gameCount,3} games"
                + (gameCount == 0 ? "   (no games yet or break week)" : "")
                + (known is null ? "" : stale ? $"   [exists, count {known.GameCount} -> {gameCount}]" : "   [exists, untouched]"));

            if (dryRun) { if (known is null) created++; else if (stale) recounted++; continue; }

            if (known is not null)
            {
                if (!stale) continue;
                known.GameCount = gameCount;
                recounted++;
                continue;
            }

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
            ? $"\nDry run: nothing written ({created} week(s) would be created, {recounted} recounted)."
            : $"\nCreated {created} week(s), recounted {recounted}; boundaries untouched.");
        return 0;
    }
}
