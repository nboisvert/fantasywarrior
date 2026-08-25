using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Clears every protection in a league back to
/// <see cref="RosterProtectionStatus.Unprotected"/>.
///
/// <b>The rule this exists to hold.</b> A protection is worth exactly one
/// off-season: it says "nobody may draft this man away from me *this summer*",
/// and it expires when the season it guarded begins (Nick, 2026-08-25). That
/// expiry is the reason the status is a column on the spot rather than a row per
/// draft — there is no history to keep, only a slate to wipe.
///
/// Nothing writes <see cref="RosterProtectionStatus.Protected"/> yet, so today
/// this job is a no-op on real data. It exists now so the rule lives in code
/// that can be run rather than in a note someone would have to rediscover next
/// July, when the protection phase lands.
///
/// **Every spot, not just the open ones.** A closed spot's status is meaningless
/// but not harmless: leaving stale flags on history would make any later "who was
/// protected" query answer for a summer that is over.
/// </summary>
public sealed class ProtectionResetJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(string? leagueCode, bool dryRun, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(leagueCode))
        {
            Console.Error.WriteLine("protection-reset needs --league <joinCode>.");
            return 1;
        }

        var league = await db.Leagues.FirstOrDefaultAsync(l => l.JoinCode == leagueCode, ct);
        if (league is null)
        {
            Console.Error.WriteLine($"No league with join code {leagueCode}.");
            return 1;
        }

        var protectedCount = await db.RosterSpots.CountAsync(
            s => s.LeagueId == league.LeagueId
                 && s.ProtectionStatus != RosterProtectionStatus.Unprotected, ct);

        Console.WriteLine($"=== protection-reset  {league.Name} ===");
        if (protectedCount == 0)
        {
            Console.WriteLine("Nothing protected. Nothing to do.");
            return 0;
        }

        if (dryRun)
        {
            Console.WriteLine($"  (dry run — would clear {protectedCount} protection(s))");
            return 0;
        }

        // A set-based UPDATE rather than loading the spots: this touches a whole
        // league's history and none of the rows are wanted individually.
        var cleared = await db.RosterSpots
            .Where(s => s.LeagueId == league.LeagueId
                        && s.ProtectionStatus != RosterProtectionStatus.Unprotected)
            .ExecuteUpdateAsync(
                u => u.SetProperty(s => s.ProtectionStatus, RosterProtectionStatus.Unprotected), ct);

        Console.WriteLine($"  Cleared {cleared} protection(s).");
        return 0;
    }
}
