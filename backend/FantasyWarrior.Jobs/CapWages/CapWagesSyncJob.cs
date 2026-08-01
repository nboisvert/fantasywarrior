using FantasyWarrior.Core.Players;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.CapWages;

/// <summary>
/// Imports real NHL contract data from CapWages into <see cref="PlayerContract"/>.
///
/// This replaces <c>estimate-salaries</c>, which scaled invented cap hits
/// across the top scorers and tagged them <c>capHitSource: "estimated"</c> so
/// nobody would mistake them for real. Every cap total, every "over budget"
/// warning and every $/point figure in the app has been built on those
/// invented numbers since July.
///
/// **Thirty-two requests, not a thousand.** Each team page carries the full
/// season-by-season contract detail for its whole roster, so the entire league
/// is covered by one page per team. Player pages exist as a fallback for
/// anyone the team pass could not match, since only they carry <c>nhlId</c>.
/// </summary>
public sealed class CapWagesSyncJob(FantasyWarriorDbContext db, CapWagesClient client)
{
    public async Task<int> RunAsync(
        string? onlySeason, bool dryRun, bool resolveUnmatched, CancellationToken ct = default)
    {
        Console.WriteLine($"=== capwages-sync{(dryRun ? "  [DRY RUN]" : "")} ===");
        if (onlySeason is not null) Console.WriteLine($"Keeping only season {onlySeason}.");

        var teams = await db.NhlTeams.OrderBy(t => t.Abbrev).ToListAsync(ct);
        Console.WriteLine($"{teams.Count} franchises to fetch, ~2s apart.\n");

        var scraped = new List<CapWagesPlayer>();
        foreach (var team in teams)
        {
            var slug = CapWagesClient.SlugFor(team.Name);
            var html = await client.GetTeamPageAsync(slug, ct);
            if (html is null)
            {
                Console.WriteLine($"  {team.Abbrev}  {slug,-26} FAILED");
                continue;
            }
            var players = CapWagesParser.ParseTeamPage(html);
            var withContracts = players.Count(p => p.Seasons.Count > 0);
            Console.WriteLine($"  {team.Abbrev}  {slug,-26} {players.Count,3} players, {withContracts,3} with contracts");
            scraped.AddRange(players);
        }

        Console.WriteLine($"\nScraped {scraped.Count} players, "
            + $"{scraped.Sum(p => p.Seasons.Count)} contract-seasons.\n");
        if (scraped.Count == 0)
        {
            Console.Error.WriteLine("Nothing scraped. Either the site changed shape or every fetch failed —"
                + " check the errors above before assuming the data is gone.");
            return 1;
        }

        // --- match to our players ---
        //
        // By nhlId when we have one (exact), otherwise by normalized name
        // within the same NHL team. Name matching across the whole league would
        // be risky; scoped to one roster it is not — two players with the same
        // name on the same team does not happen.
        var ourPlayers = await db.Players
            .Select(p => new { p.PlayerId, p.FirstName, p.LastName, p.TeamAbbrev, p.CapWagesSlug })
            .ToListAsync(ct);
        Console.WriteLine($"{ourPlayers.Count} players in our database to match against.");

        var bySlug = ourPlayers
            .Where(p => p.CapWagesSlug is not null)
            .ToDictionary(p => p.CapWagesSlug!, p => p.PlayerId);
        var byNameAndTeam = ourPlayers
            .GroupBy(p => (Name: NameNormalizer.Normalize($"{p.FirstName} {p.LastName}"), p.TeamAbbrev))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().PlayerId);
        var byNameOnly = ourPlayers
            .GroupBy(p => NameNormalizer.Normalize($"{p.FirstName} {p.LastName}"))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().PlayerId);

        var matched = new List<(long PlayerId, CapWagesPlayer Player)>();
        var unmatched = new List<CapWagesPlayer>();
        foreach (var scrapedPlayer in scraped)
        {
            var id = ResolveId(scrapedPlayer, bySlug, byNameAndTeam, byNameOnly);
            if (id is null) unmatched.Add(scrapedPlayer);
            else matched.Add((id.Value, scrapedPlayer));
        }

        // A player CapWages knows and we do not is usually someone our own
        // roster sync has not seen yet, not a bug — but a large tail means the
        // matching is broken, so it is always reported.
        Console.WriteLine($"Matched {matched.Count}, unmatched {unmatched.Count}.");
        foreach (var u in unmatched.Take(20))
            Console.WriteLine($"  ? {CapWagesParser.ToNaturalName(u.Name),-28} {u.TeamTricode,-4} ({u.Slug})");
        if (unmatched.Count > 20) Console.WriteLine($"  ... and {unmatched.Count - 20} more");

        if (resolveUnmatched && unmatched.Count > 0)
        {
            Console.WriteLine($"\nResolving {unmatched.Count} by player page (the only source of nhlId)...");
            var recovered = 0;
            foreach (var u in unmatched.ToList())
            {
                var html = await client.GetPlayerPageAsync(u.Slug, ct);
                var full = html is null ? null : CapWagesParser.ParsePlayerPage(html);
                if (full?.NhlId is not { } nhlId) continue;
                if (!ourPlayers.Any(p => p.PlayerId == nhlId)) continue;
                matched.Add((nhlId, full));
                unmatched.Remove(u);
                recovered++;
            }
            Console.WriteLine($"Recovered {recovered} by nhlId.");
        }

        if (dryRun)
        {
            Console.WriteLine("\n[DRY RUN] Nothing written. Sample of what would be:");
            foreach (var (playerId, player) in matched.Take(10))
            {
                var current = player.Seasons.MaxBy(s => s.Season);
                Console.WriteLine($"  {playerId,-9} {CapWagesParser.ToNaturalName(player.Name),-26} "
                    + $"{player.Seasons.Count,2} seasons, latest {current?.Season} @ ${current?.CapHit:N0}");
            }
            return 0;
        }

        // --- write ---
        var existing = await db.PlayerContracts
            .Where(c => c.Source == ContractSource.CapWages || c.Source == ContractSource.Estimated)
            .ToDictionaryAsync(c => (c.PlayerId, c.Season), ct);

        var now = DateTime.UtcNow;
        int inserted = 0, updated = 0, replacedEstimates = 0;
        foreach (var (playerId, player) in matched)
        {
            foreach (var season in player.Seasons)
            {
                if (onlySeason is not null && season.Season != onlySeason) continue;

                if (existing.TryGetValue((playerId, season.Season), out var row))
                {
                    // A real contract always wins over a placeholder.
                    if (row.Source == ContractSource.Estimated) replacedEstimates++;
                    else updated++;
                    Apply(row, season, now);
                }
                else
                {
                    var fresh = new PlayerContract
                    {
                        PlayerId = playerId,
                        Season = season.Season,
                        CapHit = season.CapHit,
                        Source = ContractSource.CapWages,
                        ImportedUtc = now,
                    };
                    Apply(fresh, season, now);
                    db.PlayerContracts.Add(fresh);
                    inserted++;
                }
            }

            // Remember the slug so the next run matches this player exactly,
            // and a rename on either side stops mattering.
            var playerRow = await db.Players.FirstOrDefaultAsync(p => p.PlayerId == playerId, ct);
            if (playerRow is not null && playerRow.CapWagesSlug != player.Slug)
                playerRow.CapWagesSlug = player.Slug;
        }

        await db.SaveChangesAsync(ct);
        Console.WriteLine($"\nWrote {inserted} new contract-seasons, updated {updated}, "
            + $"replaced {replacedEstimates} estimate(s) with real data.");
        return 0;
    }

    private static long? ResolveId(
        CapWagesPlayer player,
        Dictionary<string, long> bySlug,
        Dictionary<(string, string?), long> byNameAndTeam,
        Dictionary<string, long> byNameOnly)
    {
        if (player.NhlId is { } id) return id;
        if (bySlug.TryGetValue(player.Slug, out var known)) return known;

        var normalized = NameNormalizer.Normalize(CapWagesParser.ToNaturalName(player.Name));
        if (byNameAndTeam.TryGetValue((normalized, player.TeamTricode), out var byTeam)) return byTeam;
        // Last resort: a league-wide unique name. Only safe *because* it is
        // restricted to names that appear exactly once in the whole league.
        return byNameOnly.TryGetValue(normalized, out var unique) ? unique : null;
    }

    private static void Apply(PlayerContract row, CapWagesSeason season, DateTime now)
    {
        row.CapHit = season.CapHit;
        row.Aav = season.Aav;
        row.TotalValue = season.TotalSalary;
        row.ClauseType = season.Clause;
        row.Source = ContractSource.CapWages;
        row.ImportedUtc = now;
    }
}
