using System.Text.Json;
using FantasyWarrior.Core.Players;
using FantasyWarrior.Data;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Les Mordus' roster file — the one shape <c>poolexpert-parse</c> writes and
/// both <c>seed-mordus</c> and <c>reset-mordus-rosters</c> read. Players are
/// names, resolved against <c>Players</c> at load time; an entry may carry an
/// explicit <c>playerId</c> for a name the resolver cannot settle on its own.
/// </summary>
public sealed record MordusRosterFile(string Source, string Season, List<MordusRosterTeam> Teams)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<MordusRosterFile?> LoadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Roster file not found: {path}");
            return null;
        }
        return JsonSerializer.Deserialize<MordusRosterFile>(await File.ReadAllTextAsync(path, ct), Json);
    }

    /// <summary>
    /// Resolves every entry to a player id, printing each spelling correction.
    /// Null when any name is unresolved or a team lacks its username — the
    /// caller refuses rather than writing a partial roster.
    /// </summary>
    public async Task<Dictionary<RosterName, long>?> ResolveAsync(FantasyWarriorDbContext db, CancellationToken ct)
    {
        var resolver = new RosterNameResolver(await db.Players.AsNoTracking()
            .Select(p => new RosterPlayer(p.PlayerId, p.FirstName, p.LastName, p.Position, p.TeamAbbrev))
            .ToListAsync(ct));

        var resolved = new Dictionary<RosterName, long>(ReferenceEqualityComparer.Instance);
        var unresolved = new List<string>();
        var corrected = new List<string>();
        foreach (var team in Teams)
        {
            if (string.IsNullOrWhiteSpace(team.Username))
                unresolved.Add($"  {team.Franchise,-24} has no username — fill it in the file");
            foreach (var entry in team.Active.Concat(team.Reserve))
            {
                var (id, note) = resolver.Resolve(entry);
                if (id is null) { unresolved.Add($"  {team.Franchise,-24} {entry.First} {entry.Last} ({entry.Team})"); continue; }
                if (note is not null) corrected.Add($"  {note}");
                resolved[entry] = id.Value;
            }
        }

        if (corrected.Count > 0)
            Console.WriteLine($"{corrected.Count} name(s) matched by spelling correction:\n{string.Join('\n', corrected)}\n");
        if (unresolved.Count == 0) return resolved;

        Console.WriteLine($"{unresolved.Count} problem(s) in the roster file:\n{string.Join('\n', unresolved)}");
        Console.WriteLine("\nRefusing to guess. Fix the file (or run player-resolve first) and re-run.");
        return null;
    }
}

public sealed record MordusRosterTeam(
    string? Gm, string? Username, string Franchise, string FranchiseAbbrev,
    List<RosterName> Active, List<RosterName> Reserve);
