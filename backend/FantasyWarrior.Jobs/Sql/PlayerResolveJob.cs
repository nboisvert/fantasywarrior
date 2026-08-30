using FantasyWarrior.Core.Players;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Jobs.Nhl;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Adds players named in a plain-text list that <c>player-sync</c> cannot see.
///
/// **Why they are missing at all.** <c>PlayerSyncJob</c> reads two endpoints
/// per team — the season roster and the prospect list — and a player can be on
/// neither while still being someone a GM owns. An unsigned free agent is on no
/// roster; a recent draftee his club has not listed yet is on no prospect list.
/// The Mordus import hit exactly these two categories: 43 of its 360 roster
/// entries matched nothing, leaving two teams under the roster minimum and ten
/// active lineups short (.claude/doc/mordus.md).
///
/// They were never absent from the NHL's data — only out of reach of those two
/// endpoints. Every one of the 43 has an NHL id, and the search endpoint
/// returns it. Nothing here is scraped and no third-party source is involved.
///
/// **It only writes what it is certain of.** Resolution goes through
/// <see cref="PlayerSearchMatcher"/>, which answers null when nobody matches or
/// several do, and those lines are reported instead of guessed. A wrong player
/// on a keeper roster outlives the mistake by seasons — he gets traded, scored
/// and banked — whereas a missing one is visibly missing.
///
/// The row it inserts is deliberately thin: identity, position, current team.
/// Draft details, contracts and career history are the business of
/// <c>draft-sync</c>, <c>capwages-sync</c> and <c>career-sync</c>, all of which
/// pick these players up on their next run because each finds its own work by
/// querying the table.
/// </summary>
public sealed class PlayerResolveJob(NhlApiClient nhl, FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(string file, bool dryRun, CancellationToken ct = default)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Name list not found: {file}");
            return 1;
        }

        var wanted = (await File.ReadAllLinesAsync(file, ct))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"=== player-resolve  {wanted.Count} name(s){(dryRun ? "  [DRY RUN]" : "")} ===");
        if (wanted.Count == 0)
        {
            Console.WriteLine("Nothing to do.");
            return 0;
        }

        // TeamAbbrev is a foreign key: an abbreviation we have no row for would
        // fail the whole save rather than the one player.
        var knownTeams = (await db.NhlTeams.Select(t => t.Abbrev).ToListAsync(ct)).ToHashSet();
        var existing = await db.Players.ToDictionaryAsync(p => p.PlayerId, ct);
        var now = DateTime.UtcNow;

        int added = 0, already = 0;
        var unresolved = new List<string>();

        foreach (var name in wanted)
        {
            var hits = await nhl.SearchPlayersAsync(PlayerSearchMatcher.QueryFor(name), ct: ct);
            var candidates = hits
                .Where(h => h.Id is not null)
                .Select(h => new PlayerSearchCandidate(
                    h.Id!.Value, h.Name ?? "", h.PositionCode, h.TeamAbbrev, h.Active))
                .ToList();

            var id = PlayerSearchMatcher.Resolve(name, candidates);
            if (id is null)
            {
                var near = candidates.Count == 0 ? "no candidates" : $"{candidates.Count} candidates, none unambiguous";
                Console.WriteLine($"  {name,-28} UNRESOLVED ({near})");
                unresolved.Add(name);
                continue;
            }

            if (existing.ContainsKey(id.Value))
            {
                Console.WriteLine($"  {name,-28} {id} already present");
                already++;
                continue;
            }

            var hit = candidates.First(c => c.PlayerId == id.Value);
            var (first, last) = PlayerSearchMatcher.SplitName(hit.Name);
            var team = hit.TeamAbbrev is not null && knownTeams.Contains(hit.TeamAbbrev)
                ? hit.TeamAbbrev
                : null;

            var player = new Player
            {
                PlayerId = id.Value,
                FirstName = first,
                LastName = last,
                // Position drives PositionGroup, which drives lineup eligibility,
                // so it must never be blank. Same fallback as PlayerSyncJob.
                Position = string.IsNullOrWhiteSpace(hit.PositionCode) ? "C" : hit.PositionCode!,
                // "nhl" means on a team right now; an unsigned free agent is not,
                // and "prospect" is the only other value the model has. It is a
                // display label, not an eligibility rule — and player-sync will
                // correct it the day the player turns up on a real roster.
                Status = hit.Active && team is not null ? PlayerStatus.Nhl : PlayerStatus.Prospect,
                TeamAbbrev = team,
                LastSyncedUtc = now,
            };

            if (!dryRun) db.Players.Add(player);
            existing[id.Value] = player;
            added++;
            Console.WriteLine($"  {name,-28} {id}  {hit.Name} ({player.Position}, {team ?? "no team"}, {player.Status})");
        }

        if (!dryRun) await db.SaveChangesAsync(ct);

        Console.WriteLine($"\n{added} added, {already} already present, {unresolved.Count} unresolved"
            + (dryRun ? "  [DRY RUN — nothing written]" : ""));
        if (unresolved.Count > 0)
            Console.WriteLine($"To resolve by hand: {string.Join(", ", unresolved)}");

        return 0;
    }
}
