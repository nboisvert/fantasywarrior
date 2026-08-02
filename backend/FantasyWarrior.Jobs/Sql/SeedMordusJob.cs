using System.Text.Json;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Creates the real "Les Mordus" league from the rosters imported out of Nick's
/// PoolExpert standings PDF (see .claude/doc/mordus-pool.md).
///
/// The roster data is a checked-in artifact (data/mordus-rosters.json), not
/// something this job derives: the PDF's names arrive as one run of
/// concatenated text and had to be segmented against the player list offline.
/// Keeping the result in the repo makes the import reviewable and re-runnable
/// without the PDF.
///
/// Unlike the Firestore version, this seeds **roster spots too** — the model
/// that did not exist when that one was written. Every spot opens on the
/// season's first period start rather than on the day the league is created:
/// dating them from "now" is what made every team score zero the first time
/// this was tried, because every scoring window fell before the spots existed.
/// </summary>
public sealed class SeedMordusJob(FantasyWarriorDbContext db)
{
    private const string LeagueName = "Les Mordus";

    private sealed record RosterFile(string Source, ActiveSlots ActiveSlots, List<TeamEntry> Teams);
    private sealed record ActiveSlots(int Forwards, int Defense, int Goalies);
    private sealed record TeamEntry(string Gm, string Username, string Franchise, string? FranchiseAbbrev,
        List<PlayerEntry> Active, List<PlayerEntry> Reserve);
    private sealed record PlayerEntry(long PlayerId, string Name, string Pos, string Team);

    public async Task<int> RunAsync(
        string file, string season, string commissioner, long capAmount, bool dryRun,
        CancellationToken ct = default)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Roster file not found: {file}");
            return 1;
        }

        var data = JsonSerializer.Deserialize<RosterFile>(
            await File.ReadAllTextAsync(file, ct),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Console.WriteLine($"=== seed-mordus{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{data.Teams.Count} teams, slots {data.ActiveSlots.Forwards}F/"
            + $"{data.ActiveSlots.Defense}D/{data.ActiveSlots.Goalies}G, cap ${capAmount:N0}");

        if (await db.Leagues.AnyAsync(l => l.Name == LeagueName && l.Season == season, ct))
        {
            Console.Error.WriteLine($"\"{LeagueName}\" already exists for {season}. Refusing to overwrite it.");
            return 1;
        }

        // Spots must start when the season does, not when the league is created.
        var firstPeriod = await db.Periods
            .Where(p => p.Season == season)
            .OrderBy(p => p.Number)
            .FirstOrDefaultAsync(ct);
        if (firstPeriod is null)
        {
            Console.Error.WriteLine($"No period calendar for {season}. Run sql-period-init first.");
            return 1;
        }
        Console.WriteLine($"Roster spots open on {firstPeriod.StartDate:yyyy-MM-dd} (week 1 start).\n");

        // Every player id is verified before anything is written: a bad id would
        // otherwise fail the save halfway and leave a half-built league.
        var wanted = data.Teams
            .SelectMany(t => t.Active.Concat(t.Reserve))
            .Select(p => p.PlayerId)
            .Distinct()
            .ToList();
        var known = (await db.Players.Where(p => wanted.Contains(p.PlayerId))
            .Select(p => p.PlayerId).ToListAsync(ct)).ToHashSet();
        var missing = wanted.Where(id => !known.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            Console.Error.WriteLine($"{missing.Count} player id(s) not in the database — run sql-player-sync first:");
            foreach (var t in data.Teams)
                foreach (var p in t.Active.Concat(t.Reserve).Where(p => missing.Contains(p.PlayerId)))
                    Console.Error.WriteLine($"  {p.PlayerId}  {p.Name} ({p.Pos}, {p.Team})");
            return 1;
        }

        var positions = await db.Players
            .Where(p => wanted.Contains(p.PlayerId))
            .ToDictionaryAsync(p => p.PlayerId, p => p.Position, ct);

        if (dryRun)
        {
            Console.WriteLine($"[DRY RUN] Would create {data.Teams.Count} teams and "
                + $"{data.Teams.Sum(t => t.Active.Count + t.Reserve.Count)} roster spots. Nothing written.");
            return 0;
        }

        var now = DateTime.UtcNow;
        var commissionerUser = await UpsertUserAsync(commissioner, now, ct);

        var league = new League
        {
            Name = LeagueName,
            Season = season,
            JoinCode = await UniqueJoinCodeAsync(ct),
            CommissionerUserId = commissionerUser.UserId,
            CapAmount = capAmount,
            RosterMin = 23,
            RosterMax = 35,
            ActiveForwards = data.ActiveSlots.Forwards,
            ActiveDefense = data.ActiveSlots.Defense,
            ActiveGoalies = data.ActiveSlots.Goalies,
            CreatedUtc = now,
        };
        db.Leagues.Add(league);
        await db.SaveChangesAsync(ct);

        // The five historic values, as rows. Anything else a commissioner wants
        // to score is another row, never a schema change.
        foreach (var (key, value) in new (string, double)[]
                 {
                     (StatKeys.Goals, 1), (StatKeys.Assists, 1),
                     (StatKeys.Wins, 2), (StatKeys.OtLosses, 1), (StatKeys.Shutouts, 0),
                 })
            db.LeagueScoringRules.Add(new LeagueScoringRule
            {
                LeagueId = league.LeagueId, StatKey = key, PointValue = value,
            });

        var spotCount = 0;
        foreach (var entry in data.Teams)
        {
            var user = await UpsertUserAsync(entry.Username, now, ct, entry.Gm);
            var team = new Team
            {
                LeagueId = league.LeagueId,
                OwnerUserId = user.UserId,
                Name = entry.Franchise,
                FranchiseAbbrev = entry.FranchiseAbbrev,
                CreatedUtc = now,
            };
            db.Teams.Add(team);
            db.LeagueMembers.Add(new LeagueMember
            {
                LeagueId = league.LeagueId, UserId = user.UserId, JoinedUtc = now,
            });
            await db.SaveChangesAsync(ct);

            foreach (var player in entry.Active.Concat(entry.Reserve))
            {
                db.RosterSpots.Add(new RosterSpot
                {
                    LeagueId = league.LeagueId,
                    TeamId = team.TeamId,
                    PlayerId = player.PlayerId,
                    PositionGroup = PositionGroups.CodeFrom(positions.GetValueOrDefault(player.PlayerId, "C")),
                    StartDate = firstPeriod.StartDate,
                    StartReason = RosterSpotStartReason.Draft,
                    OpenedUtc = now,
                });
                spotCount++;
            }
            await db.SaveChangesAsync(ct);

            Console.WriteLine($"  {entry.Username,-12} {entry.Franchise,-24} "
                + $"{entry.Active.Count + entry.Reserve.Count,2} players"
                + (entry.FranchiseAbbrev is null ? "" : $"  [{entry.FranchiseAbbrev}]"));
        }

        Console.WriteLine($"\nCreated \"{LeagueName}\" ({league.Season}) — join code {league.JoinCode}, "
            + $"{data.Teams.Count} teams, {spotCount} roster spots.");
        return 0;
    }

    private async Task<User> UpsertUserAsync(
        string username, DateTime now, CancellationToken ct, string? displayName = null)
    {
        var normalized = username.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct);
        if (user is not null) return user;

        user = new User
        {
            Username = normalized,
            DisplayName = displayName ?? username.Trim(),
            CreatedUtc = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    private async Task<string> UniqueJoinCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = JoinCodes.New();
            if (!await db.Leagues.AnyAsync(l => l.JoinCode == code, ct)) return code;
        }
        throw new InvalidOperationException("Could not generate a unique join code in 10 attempts.");
    }
}
