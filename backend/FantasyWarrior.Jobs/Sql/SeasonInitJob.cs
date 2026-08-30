using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Season = FantasyWarrior.Core.Seasons.Season;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Declares an NHL season's dates — the system-wide calendar every league runs
/// on. Upsert, so re-running with corrected dates is the supported way to fix
/// one.
///
/// <b>Run by hand, from the schedule the NHL publishes.</b> Roughly one row a
/// year, and the dates are announced long before any of them can be observed —
/// which is the whole point: <c>period-init</c> can build a season's weekly
/// calendar the day this row exists, instead of waiting for games to be synced.
///
/// Unlike every other date in this app these are not derived from anything, so
/// the two invariants worth holding are checked here: the season string must be
/// a real one (the column is free text, and <c>"2025-2026"</c> would otherwise
/// create a phantom season), and the regular season must not end before it
/// starts.
/// </summary>
public sealed class SeasonInitJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(
        string? season, string? start, string? end, string? playoffStart, string? playoffEnd,
        bool dryRun, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            await ListAsync(ct);
            return 0;
        }

        if (!Season.IsValid(season))
        {
            Console.Error.WriteLine(
                $"\"{season}\" is not a valid NHL season. Expected eight digits whose second half "
                + "succeeds the first, e.g. 20262027.");
            return 1;
        }

        var existing = await db.Seasons.FirstOrDefaultAsync(s => s.Season == season, ct);

        // No dates given: this is a read, not a write. Saying so beats silently
        // rewriting a row with whatever the defaults would have been.
        if (start is null && end is null)
        {
            if (existing is null)
            {
                Console.Error.WriteLine(
                    $"{season} is not declared, and --start/--end were not given. "
                    + $"season-init --season {season} --start YYYY-MM-DD --end YYYY-MM-DD");
                return 1;
            }
            Console.WriteLine($"=== season-init {Season.Display(season)} ===");
            Print(existing);
            return 0;
        }

        if (!TryDate(start, "--start", out var startDate) || !TryDate(end, "--end", out var endDate))
            return 1;
        if (startDate is null || endDate is null)
        {
            Console.Error.WriteLine("--start and --end must be given together.");
            return 1;
        }
        if (endDate < startDate)
        {
            Console.Error.WriteLine($"The regular season cannot end ({endDate}) before it starts ({startDate}).");
            return 1;
        }
        if (!TryDate(playoffStart, "--playoff-start", out var poStart)
            || !TryDate(playoffEnd, "--playoff-end", out var poEnd))
            return 1;
        if (poStart is not null && poStart < endDate)
        {
            Console.Error.WriteLine(
                $"The playoffs cannot start ({poStart}) before the regular season ends ({endDate}).");
            return 1;
        }
        if (poEnd is not null && poStart is not null && poEnd < poStart)
        {
            Console.Error.WriteLine($"The playoffs cannot end ({poEnd}) before they start ({poStart}).");
            return 1;
        }

        Console.WriteLine($"=== season-init {Season.Display(season)}{(dryRun ? "  [DRY RUN]" : "")} ===");
        if (existing is not null)
        {
            Console.WriteLine("Currently declared:");
            Print(existing);
        }

        var row = existing ?? new NhlSeason
        {
            Season = season,
            RegularSeasonStart = startDate.Value,
            RegularSeasonEnd = endDate.Value,
        };
        row.RegularSeasonStart = startDate.Value;
        row.RegularSeasonEnd = endDate.Value;
        // Only overwrite a playoff date that was actually supplied: correcting
        // the regular season must not silently erase a bracket already entered.
        if (poStart is not null) row.PlayoffStart = poStart;
        if (poEnd is not null) row.PlayoffEnd = poEnd;

        Console.WriteLine(existing is null ? "Declaring:" : "Now declared:");
        Print(row);

        if (dryRun)
        {
            Console.WriteLine("\nDry run: nothing written.");
            return 0;
        }

        if (existing is null) db.Seasons.Add(row);
        await db.SaveChangesAsync(ct);
        Console.WriteLine($"\n{(existing is null ? "Created" : "Updated")} {season}. "
            + $"Run `period-init --season {season}` to build its weekly calendar.");
        return 0;
    }

    private async Task ListAsync(CancellationToken ct)
    {
        var rows = await db.Seasons.AsNoTracking().OrderBy(s => s.Season).ToListAsync(ct);
        Console.WriteLine("=== season-init ===");
        if (rows.Count == 0)
        {
            Console.WriteLine("No season is declared. Jobs fall back to the September-cutover guess.");
            return;
        }
        foreach (var row in rows) Print(row);
    }

    private static void Print(NhlSeason s) =>
        Console.WriteLine(
            $"  {Season.Display(s.Season)}  regular {s.RegularSeasonStart:yyyy-MM-dd} -> {s.RegularSeasonEnd:yyyy-MM-dd}"
            + $"   playoffs {Show(s.PlayoffStart)} -> {Show(s.PlayoffEnd)}"
            + (s.ScheduleImportedUtc is { } at ? $"   schedule imported {at:yyyy-MM-dd}" : "   schedule not imported"));

    private static string Show(DateOnly? date) => date?.ToString("yyyy-MM-dd") ?? "—";

    private static bool TryDate(string? raw, string option, out DateOnly? date)
    {
        date = null;
        if (raw is null) return true;
        if (DateOnly.TryParse(raw, out var parsed)) { date = parsed; return true; }

        Console.Error.WriteLine($"{option} must be a date, e.g. 2026-10-06. Got \"{raw}\".");
        return false;
    }
}
