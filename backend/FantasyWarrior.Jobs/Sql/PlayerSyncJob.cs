using System.Globalization;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Jobs.Nhl;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Refreshes <see cref="Player"/> from the NHL API: every team's season roster
/// plus its prospects.
///
/// **Owns only the fields it syncs.** Draft details come from
/// <c>draft-sync</c>, the CapWages slug from <c>capwages-sync</c>, and salary
/// now lives in its own table entirely. Under Firestore this needed a
/// hand-maintained <c>SetOptions.MergeFields</c> list that had to be kept in
/// step with the model by memory — here it is just "assign these properties on
/// the tracked entity", and anything not assigned is untouched by construction.
///
/// A roster entry beats a prospect entry when a player appears as both, which
/// is why prospects are read first.
/// </summary>
public sealed class PlayerSyncJob(NhlApiClient nhl, FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(string season, bool dryRun = false, CancellationToken ct = default)
    {
        var teams = await nhl.GetTeamAbbrevsAsync(ct);
        Console.WriteLine($"=== player-sync  season {season}  {teams.Count} teams{(dryRun ? "  [DRY RUN]" : "")} ===");

        // Only franchises we actually have a row for: TeamAbbrev is a foreign
        // key, so an unknown code would fail the whole save rather than the one
        // player.
        var known = (await db.NhlTeams.Select(t => t.Abbrev).ToListAsync(ct)).ToHashSet();

        var scraped = new Dictionary<long, (NhlPlayerDto Dto, string Team, string Status)>();
        foreach (var team in teams)
        {
            if (!known.Contains(team))
            {
                Console.WriteLine($"  {team}: not a franchise we know — skipped");
                continue;
            }

            foreach (var dto in await nhl.GetProspectsAsync(team, ct))
                scraped[dto.Id] = (dto, team, PlayerStatus.Prospect);

            var roster = await nhl.GetRosterAsync(team, season, ct);
            if (roster.Count == 0)
            {
                // Off-season: next season's roster may not be published yet.
                var previous = PreviousSeason(season);
                roster = await nhl.GetRosterAsync(team, previous, ct);
                if (roster.Count > 0)
                    Console.WriteLine($"  {team}: season {season} not published, using {previous}");
            }
            foreach (var dto in roster)
                scraped[dto.Id] = (dto, team, PlayerStatus.Nhl);

            Console.WriteLine($"  {team}: roster {roster.Count,3}, running total {scraped.Count}");
        }

        if (scraped.Count == 0)
        {
            Console.Error.WriteLine("No players returned. The NHL API is unreachable or has changed shape —"
                + " not writing anything rather than emptying the table.");
            return 1;
        }

        var existing = await db.Players.ToDictionaryAsync(p => p.PlayerId, ct);
        var now = DateTime.UtcNow;
        int inserted = 0, updated = 0;

        foreach (var (id, (dto, team, status)) in scraped)
        {
            if (existing.TryGetValue(id, out var player))
            {
                Apply(player, dto, team, status, now);
                updated++;
            }
            else
            {
                player = new Player
                {
                    PlayerId = id,
                    FirstName = "",
                    LastName = "",
                    Position = "",
                    Status = status,
                };
                Apply(player, dto, team, status, now);
                db.Players.Add(player);
                inserted++;
            }
        }

        if (dryRun)
        {
            Console.WriteLine($"\n[DRY RUN] Would insert {inserted}, update {updated}. Nothing written.");
            return 0;
        }

        await db.SaveChangesAsync(ct);
        Console.WriteLine($"\nInserted {inserted}, updated {updated} — {scraped.Count} players total.");
        return 0;
    }

    private static void Apply(Player player, NhlPlayerDto dto, string teamAbbrev, string status, DateTime now)
    {
        player.FirstName = dto.FirstName?.Default ?? "";
        player.LastName = dto.LastName?.Default ?? "";
        // The NHL occasionally returns a blank position for a prospect. Keeping
        // whatever we had beats overwriting it with nothing, since PositionGroup
        // is computed from it and drives lineup eligibility.
        if (!string.IsNullOrWhiteSpace(dto.PositionCode)) player.Position = dto.PositionCode;
        else if (string.IsNullOrEmpty(player.Position)) player.Position = "C";
        player.TeamAbbrev = teamAbbrev;
        player.Status = status;
        player.SweaterNumber = dto.SweaterNumber;
        player.ShootsCatches = Trim(dto.ShootsCatches, 1);
        player.BirthDate = ParseDate(dto.BirthDate);
        player.BirthCountry = Trim(dto.BirthCountry, 3);
        player.HeightCm = dto.HeightInCentimeters;
        player.WeightKg = dto.WeightInKilograms;
        player.HeadshotUrl = dto.Headshot;
        player.LastSyncedUtc = now;
    }

    private static DateOnly? ParseDate(string? iso) =>
        DateOnly.TryParse(iso, CultureInfo.InvariantCulture, out var date) ? date : null;

    /// <summary>Guards the fixed-length columns against a surprise from the feed.</summary>
    private static string? Trim(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= max ? value
        : value[..max];

    private static string PreviousSeason(string season)
    {
        var startYear = int.Parse(season[..4]) - 1;
        return $"{startYear}{startYear + 1}";
    }
}
