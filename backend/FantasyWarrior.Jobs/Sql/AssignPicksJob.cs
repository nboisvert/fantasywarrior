using System.Text.Json;
using FantasyWarrior.Data;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Reassigns a batch of already-initialized draft picks to whoever actually
/// holds them, from a JSON file of {round, originalAbbrev, ownerUsername}
/// entries — the league's own real trade history, reconciled by hand against
/// a source outside the app (a spreadsheet, a commissioner's own records)
/// rather than replayed trade by trade.
///
/// A pick not listed keeps whatever <c>draft-picks-init</c> gave it — its
/// original team, i.e. never traded — so the file only needs the exceptions.
/// </summary>
public sealed class AssignPicksJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(string leagueCode, int year, string filePath, bool dryRun, CancellationToken ct = default)
    {
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.JoinCode == leagueCode, ct);
        if (league is null) { Console.Error.WriteLine($"No league with join code {leagueCode}."); return 1; }

        if (!File.Exists(filePath)) { Console.Error.WriteLine($"No file at {filePath}."); return 1; }
        var entries = JsonSerializer.Deserialize<List<PickAssignment>>(
            await File.ReadAllTextAsync(filePath, ct),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var teams = await db.Teams.Where(t => t.LeagueId == league.LeagueId).Include(t => t.Owner).ToListAsync(ct);
        var byAbbrev = teams.Where(t => t.FranchiseAbbrev is not null).ToDictionary(t => t.FranchiseAbbrev!);
        var byUsername = teams.Where(t => t.Owner is not null).ToDictionary(t => t.Owner!.Username);

        var picks = await db.DraftPicks.Where(p => p.LeagueId == league.LeagueId && p.Year == year).ToListAsync(ct);

        Console.WriteLine($"=== assign-picks{(dryRun ? "  [DRY RUN]" : "")}  {league.Name}, {year} ===");

        var errors = new List<string>();
        var changes = new List<(FantasyWarrior.Data.Entities.DraftPick pick, string from, string to)>();
        foreach (var e in entries)
        {
            if (!byAbbrev.TryGetValue(e.OriginalAbbrev, out var originalTeam))
            {
                errors.Add($"Unknown franchise abbrev '{e.OriginalAbbrev}'.");
                continue;
            }
            if (!byUsername.TryGetValue(e.OwnerUsername, out var ownerTeam))
            {
                errors.Add($"Unknown owner username '{e.OwnerUsername}'.");
                continue;
            }
            var pick = picks.FirstOrDefault(p => p.Round == e.Round && p.OriginalTeamId == originalTeam.TeamId);
            if (pick is null)
            {
                errors.Add($"No {year} round {e.Round} pick found for {e.OriginalAbbrev} — was draft-picks-init run?");
                continue;
            }
            if (pick.CurrentTeamId == ownerTeam.TeamId)
                continue; // Already correct — draft-picks-init's default agrees with the file.

            var fromTeam = teams.First(t => t.TeamId == pick.CurrentTeamId);
            changes.Add((pick, fromTeam.Owner!.Username, e.OwnerUsername));
            if (!dryRun) pick.CurrentTeamId = ownerTeam.TeamId;
        }

        foreach (var c in changes)
            Console.WriteLine($"  {c.pick.Year} rd {c.pick.Round} ({byAbbrev.First(kv => kv.Value.TeamId == c.pick.OriginalTeamId).Key}): "
                + $"{c.from} -> {c.to}");
        if (errors.Count > 0)
        {
            Console.WriteLine("\nErrors:");
            foreach (var err in errors) Console.WriteLine($"  {err}");
        }
        Console.WriteLine($"\n{changes.Count} reassignment(s), {errors.Count} error(s), "
            + $"{entries.Count - changes.Count - errors.Count} already correct.");

        if (dryRun) { Console.WriteLine("[DRY RUN] Nothing written."); return errors.Count > 0 ? 1 : 0; }
        if (errors.Count > 0) { Console.Error.WriteLine("Refusing to save: fix the errors above first."); return 1; }

        await db.SaveChangesAsync(ct);
        return 0;
    }

    private sealed record PickAssignment(int Round, string OriginalAbbrev, string OwnerUsername);
}
