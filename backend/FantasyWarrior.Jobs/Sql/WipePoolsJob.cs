using FantasyWarrior.Data;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Deletes everything pool-related, leaving the NHL reference data untouched.
///
/// The split matters: players, games, game lines and contracts cost hours to
/// re-import and are identical for everyone, while leagues and rosters are
/// cheap to recreate from the seed file. Wiping the first kind by accident is
/// the expensive mistake, so this job cannot do it.
///
/// Order is explicit rather than relying on cascades. Most foreign keys here
/// are NO ACTION on purpose — a stray delete should fail loudly instead of
/// quietly taking a league's history with it — which means the sequence below
/// is the only way through.
/// </summary>
public sealed class WipePoolsJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(bool dryRun, CancellationToken ct = default)
    {
        Console.WriteLine($"=== wipe-pools{(dryRun ? "  [DRY RUN]" : "")} ===");

        var counts = new (string Name, int Count)[]
        {
            ("TradeVotes", await db.TradeVotes.CountAsync(ct)),
            ("TradeAssets", await db.TradeAssets.CountAsync(ct)),
            ("Trades", await db.Trades.CountAsync(ct)),
            ("RosterAssignments", await db.RosterAssignments.CountAsync(ct)),
            ("TeamPeriodLineups", await db.TeamPeriodLineups.CountAsync(ct)),
            ("DraftPicks", await db.DraftPicks.CountAsync(ct)),
            ("RosterSpots", await db.RosterSpots.CountAsync(ct)),
            ("Teams", await db.Teams.CountAsync(ct)),
            ("LeagueScoringRules", await db.LeagueScoringRules.CountAsync(ct)),
            ("LeagueMembers", await db.LeagueMembers.CountAsync(ct)),
            ("Leagues", await db.Leagues.CountAsync(ct)),
            ("Users", await db.Users.CountAsync(ct)),
        };
        foreach (var (name, count) in counts) Console.WriteLine($"  {name,-20} {count,6}");

        Console.WriteLine("\n  (kept: Players, PlayerContracts, Games, PlayerGameStats, Periods, NhlTeams, NewsItems)");
        if (dryRun) { Console.WriteLine("\n[DRY RUN] Nothing deleted."); return 0; }

        // Children before parents, in one transaction so a failure part-way
        // cannot leave a league with no teams or a team with no owner.
        //
        // Through the execution strategy, because retries are enabled: the
        // serverless tier drops connections when it resumes, and a retry has to
        // replay the whole transaction rather than half of it.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.TradeVotes.ExecuteDeleteAsync(ct);
            await db.TradeAssets.ExecuteDeleteAsync(ct);
            await db.Trades.ExecuteDeleteAsync(ct);
            await db.RosterAssignments.ExecuteDeleteAsync(ct);
            await db.TeamPeriodLineups.ExecuteDeleteAsync(ct);
            await db.DraftPicks.ExecuteDeleteAsync(ct);
            await db.RosterSpots.ExecuteDeleteAsync(ct);
            await db.Teams.ExecuteDeleteAsync(ct);
            await db.LeagueScoringRules.ExecuteDeleteAsync(ct);
            await db.LeagueMembers.ExecuteDeleteAsync(ct);
            await db.Leagues.ExecuteDeleteAsync(ct);
            await db.Users.ExecuteDeleteAsync(ct);
            // The simulation cursor belongs to a pool run, not to the NHL data.
            await db.SimulationState.ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });

        Console.WriteLine("\nPool data wiped. NHL reference data untouched.");
        return 0;
    }
}
