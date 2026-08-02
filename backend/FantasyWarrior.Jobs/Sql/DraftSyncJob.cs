using FantasyWarrior.Data;
using FantasyWarrior.Jobs.Nhl;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Backfills NHL entry draft details (year, round, overall pick, drafting team).
///
/// Separate from the roster sync because it is one HTTP call per player: the
/// team roster and prospect endpoints do not carry draft information, only the
/// per-player landing page does.
///
/// <c>DraftChecked</c> is what keeps it cheap. "We looked and he was undrafted"
/// and "we never looked" are different states, and only the second is worth
/// re-querying — so after the first backfill this touches new players only.
/// </summary>
public sealed class DraftSyncJob(NhlApiClient nhl, FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(int? limit = null, CancellationToken ct = default)
    {
        var pending = await db.Players
            .Where(p => !p.DraftChecked)
            .OrderBy(p => p.PlayerId)
            .Take(limit ?? int.MaxValue)
            .ToListAsync(ct);

        Console.WriteLine($"=== draft-sync  {pending.Count} player(s) never checked ===");
        if (pending.Count == 0)
        {
            Console.WriteLine("Nothing to do.");
            return 0;
        }

        int drafted = 0, undrafted = 0, failed = 0;
        foreach (var (player, index) in pending.Select((p, i) => (p, i)))
        {
            var landing = await nhl.GetPlayerLandingAsync(player.PlayerId, ct);
            if (landing is null)
            {
                // Leave DraftChecked false so a later run retries — a network
                // blip must not be recorded as "this player is undrafted".
                failed++;
                continue;
            }

            var d = landing.DraftDetails;
            player.DraftYear = d?.Year;
            player.DraftRound = d?.Round;
            player.DraftOverall = d?.OverallPick;
            player.DraftTeamAbbrev = d?.TeamAbbrev?.Length == 3 ? d.TeamAbbrev : null;
            player.DraftChecked = true;

            if (d?.Year is not null) drafted++; else undrafted++;

            if ((index + 1) % 100 == 0)
            {
                await db.SaveChangesAsync(ct);
                Console.WriteLine($"  {index + 1}/{pending.Count} — {drafted} drafted, {undrafted} undrafted, {failed} failed");
            }
        }

        await db.SaveChangesAsync(ct);
        Console.WriteLine($"\n{drafted} drafted, {undrafted} undrafted, {failed} left for a later run.");
        return 0;
    }
}
