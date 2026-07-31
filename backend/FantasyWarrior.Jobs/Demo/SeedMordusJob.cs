using System.Text.Json;
using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Users;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Demo;

/// <summary>
/// Creates the real "Les Mordus" league from the rosters imported out of
/// Nick's PoolExpert standings PDF (see .claude/doc/mordus-pool.md).
///
/// The roster data is a checked-in artifact (data/mordus-rosters.json), not
/// something this job derives: the PDF's player names arrive as one run of
/// concatenated text and had to be segmented against the players collection
/// offline. Keeping the result in the repo makes the import reviewable and
/// re-runnable without the PDF.
///
/// Scope note: this seeds users, the league, and each team's roster. It does
/// NOT create roster spots or weekly lineups -- those models don't exist yet
/// (commits C4/C5 of the scoring refactor). The active/bench split is
/// preserved in the JSON, so the first week's lineup can be materialized
/// from the same file once Lineup lands. Nothing is lost by seeding now.
/// </summary>
public sealed class SeedMordusJob(FirestoreDb db)
{
    private const string LeagueName = "Les Mordus";

    private sealed record RosterFile(string Source, ActiveSlots ActiveSlots, List<TeamEntry> Teams);
    private sealed record ActiveSlots(int Forwards, int Defense, int Goalies);
    private sealed record TeamEntry(string Gm, string Username, string Franchise, string? FranchiseAbbrev,
        List<PlayerEntry> Active, List<PlayerEntry> Reserve);
    private sealed record PlayerEntry(long PlayerId, string Name, string Pos, string Team);

    public async Task RunAsync(
        string file, string season, string commissioner, long capAmount, bool dryRun, CancellationToken ct = default)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Roster file not found: {file}");
            return;
        }

        var data = JsonSerializer.Deserialize<RosterFile>(
            await File.ReadAllTextAsync(file, ct),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Could not parse {file}");

        Console.WriteLine($"=== Seed '{LeagueName}' season {season}{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"Source: {data.Source}");
        Console.WriteLine($"Active slots: {data.ActiveSlots.Forwards}F/{data.ActiveSlots.Defense}D/{data.ActiveSlots.Goalies}G, cap ${capAmount:N0}");

        if (data.Teams.All(t => t.Username != commissioner))
        {
            Console.Error.WriteLine($"Commissioner '{commissioner}' is not one of the teams: {string.Join(", ", data.Teams.Select(t => t.Username))}");
            return;
        }

        // Fail before writing anything if the roster references players that
        // aren't in the collection -- a silently short roster is worse than a
        // refused import.
        var allIds = data.Teams.SelectMany(t => t.Active.Concat(t.Reserve)).Select(p => p.PlayerId).Distinct().ToList();
        var known = await ExistingPlayerIdsAsync(allIds, ct);
        var unknown = allIds.Where(id => !known.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"{unknown.Count} player id(s) in the roster file are absent from `players`: {string.Join(", ", unknown.Take(10))}");
            return;
        }
        Console.WriteLine($"Verified {allIds.Count} distinct players against the players collection.");

        foreach (var t in data.Teams)
        {
            var f = Count(t.Active, "F");
            var d = Count(t.Active, "D");
            var g = Count(t.Active, "G");
            var full = f == data.ActiveSlots.Forwards && d == data.ActiveSlots.Defense && g == data.ActiveSlots.Goalies;
            Console.WriteLine($"  {(full ? " " : "!")} {t.Username,-10} {t.Gm,-26} {t.Franchise} ({t.FranchiseAbbrev})  "
                + $"actif {f}F/{d}D/{g}G  reserve {t.Reserve.Count}  total {t.Active.Count + t.Reserve.Count}");
        }

        var incomplete = data.Teams.Count(t =>
            Count(t.Active, "F") != data.ActiveSlots.Forwards
            || Count(t.Active, "D") != data.ActiveSlots.Defense
            || Count(t.Active, "G") != data.ActiveSlots.Goalies);
        if (incomplete > 0)
            Console.WriteLine($"  ({incomplete} team(s) marked '!' are short an active player -- see the unmatched register in .claude/doc/mordus-pool.md)");

        if (dryRun)
        {
            Console.WriteLine("Dry run: nothing written.");
            return;
        }

        var existing = (await db.Collection("leagues").WhereEqualTo("name", LeagueName).Limit(1).GetSnapshotAsync(ct))
            .Documents.FirstOrDefault();
        if (existing is not null)
        {
            Console.Error.WriteLine($"League '{LeagueName}' already exists ({existing.Id}). Delete it first -- this job refuses to overwrite.");
            return;
        }

        var now = Timestamp.GetCurrentTimestamp();
        foreach (var t in data.Teams)
            await db.Collection("users").Document(t.Username).SetAsync(
                new User { DisplayName = t.Gm, CreatedUtc = now, LastLoginUtc = now }, cancellationToken: ct);
        Console.WriteLine($"Created {data.Teams.Count} users.");

        var leagueDoc = db.Collection("leagues").Document();
        await leagueDoc.SetAsync(new League
        {
            Name = LeagueName,
            Season = season,
            CommissionerUsername = commissioner,
            MemberUsernames = [.. data.Teams.Select(t => t.Username)],
            // Point values are NOT yet known for this league -- defaults stand
            // in until Nick supplies the real scale. TopCount is left null so
            // nothing is silently excluded from scoring in the meantime.
            RuleConfig = new RuleConfig
            {
                RosterSize = new RosterSize { Min = null, Max = null },
            },
            CapAmount = capAmount,
            CreatedUtc = now,
        }, cancellationToken: ct);

        foreach (var t in data.Teams)
        {
            await leagueDoc.Collection("teams").Document(t.Username).SetAsync(new Team
            {
                Name = t.Franchise,
                OwnerUsername = t.Username,
                FranchiseAbbrev = t.FranchiseAbbrev,
                PlayerIds = [.. t.Active.Concat(t.Reserve).Select(p => p.PlayerId)],
                CreatedUtc = now,
            }, cancellationToken: ct);
        }

        Console.WriteLine($"Created league {leagueDoc.Id} with {data.Teams.Count} teams.");
        Console.WriteLine($"=== Done. League id: {leagueDoc.Id} ===");
        Console.WriteLine("Point values are still the defaults -- set them via the commissioner rules screen once known.");
    }

    private static int Count(List<PlayerEntry> players, string group) =>
        players.Count(p => (p.Pos == "D" ? "D" : p.Pos == "G" ? "G" : "F") == group);

    private async Task<HashSet<long>> ExistingPlayerIdsAsync(List<long> ids, CancellationToken ct)
    {
        var found = new HashSet<long>();
        foreach (var chunk in ids.Chunk(300))
        {
            var refs = chunk.Select(id => db.Collection("players").Document(id.ToString())).ToArray();
            foreach (var snap in await db.GetAllSnapshotsAsync(refs, ct))
                if (snap.Exists) found.Add(long.Parse(snap.Id));
        }
        return found;
    }
}
