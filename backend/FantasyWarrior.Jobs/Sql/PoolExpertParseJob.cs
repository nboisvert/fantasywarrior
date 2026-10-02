using System.Text.Json;
using FantasyWarrior.Core.Imports;
using FantasyWarrior.Data;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Writes a <see cref="MordusRosterFile"/> from a PoolExpert report extracted
/// with <c>pdftotext -raw -enc UTF-8</c>. Reads only <c>NhlTeams</c>, for the
/// franchise's full name; writes nothing to the database.
///
/// Usernames and GM display names are left null: PoolExpert's participant
/// header runs the GM's name into his city ("ALEXANDRE GIGUERE BRIERE NEW
/// JERSEY"), so no rule splits it reliably, and both seed jobs refuse a team
/// without a username — the file is reviewed and completed by hand.
/// </summary>
public sealed class PoolExpertParseJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(string textFile, string outFile, string season, CancellationToken ct = default)
    {
        if (!File.Exists(textFile))
        {
            Console.Error.WriteLine($"Report text not found: {textFile}");
            return 1;
        }

        var parse = PoolExpertReport.Parse(await File.ReadAllTextAsync(textFile, ct));
        var names = await db.NhlTeams.AsNoTracking().ToDictionaryAsync(t => t.Abbrev, t => t.Name, ct);

        var problems = new List<string>();
        var teams = new List<MordusRosterTeam>();
        foreach (var t in parse.Teams)
        {
            if (t.FranchiseAbbrev is not { } abbrev || !names.TryGetValue(abbrev, out var franchise))
            {
                problems.Add($"  {t.Participant}: no franchise line, or an unknown abbrev ({t.FranchiseAbbrev})");
                continue;
            }
            teams.Add(new MordusRosterTeam(null, null, franchise, abbrev, t.Active, t.Reserve));
            Console.WriteLine($"  {t.Participant,-40} [{abbrev}] {t.Active.Count,2} active, {t.Reserve.Count,2} reserve");
        }

        if (parse.Skipped.Count > 0)
            Console.WriteLine($"\n{parse.Skipped.Count} garbled line(s) skipped:\n  {string.Join("\n  ", parse.Skipped)}");
        if (problems.Count > 0)
        {
            Console.Error.WriteLine($"\n{problems.Count} participant(s) without a usable franchise:\n{string.Join('\n', problems)}");
            return 1;
        }

        var file = new MordusRosterFile(Path.GetFileName(textFile), season, teams);
        await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(file, MordusRosterFile.Json), ct);
        Console.WriteLine($"\nWrote {outFile}: {teams.Count} teams. Fill in each team's username and gm before seeding.");
        return 0;
    }
}
