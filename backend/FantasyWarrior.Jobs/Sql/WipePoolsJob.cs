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
            ("Messages", await db.Messages.CountAsync(ct)),
            ("CockcoinAwards", await db.CockcoinAwards.CountAsync(ct)),
            ("TradeVotes", await db.TradeVotes.CountAsync(ct)),
            ("TradeAssets", await db.TradeAssets.CountAsync(ct)),
            ("Trades", await db.Trades.CountAsync(ct)),
            ("RosterAssignments", await db.RosterAssignments.CountAsync(ct)),
            ("TeamPeriodLineups", await db.TeamPeriodLineups.CountAsync(ct)),
            ("DraftPicks", await db.DraftPicks.CountAsync(ct)),
            ("RosterSpots", await db.RosterSpots.CountAsync(ct)),
            ("Teams", await db.Teams.CountAsync(ct)),
            ("LeagueSeasons", await db.LeagueSeasons.CountAsync(ct)),
            ("LeagueMembers", await db.LeagueMembers.CountAsync(ct)),
            ("Leagues", await db.Leagues.CountAsync(ct)),
            ("Users", await db.Users.CountAsync(ct)),
        };
        foreach (var (name, count) in counts) Console.WriteLine($"  {name,-20} {count,6}");

        Console.WriteLine($"  {"Periods (un-banked)",-20} "
            + $"{await db.Periods.CountAsync(p => p.FinalizedUtc != null, ct),6}");
        Console.WriteLine("\n  (kept: Players, PlayerContracts, Games, PlayerGameStats, NhlTeams, NewsItems,"
            + " and the period calendar itself)");
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

            // A topological order over the pool tables' foreign keys, not a
            // guess at one. Two things had drifted out of it and both failed
            // here rather than anywhere quieter:
            //
            //   - Messages and CockcoinAwards arrived after this job was
            //     written and were never added to it at all;
            //   - RosterSpots.EndTradeId means a spot outlives the trade that
            //     closed it, so spots have to go before trades — the old order
            //     had it the other way round.
            //
            // Deleting Trades before RosterSpots is the kind of mistake that
            // only shows up on a real wipe, and the last one was in July.
            await db.RosterAssignments.ExecuteDeleteAsync(ct);
            await db.RosterSpots.ExecuteDeleteAsync(ct);
            await db.TradeAssets.ExecuteDeleteAsync(ct);
            await db.TradeVotes.ExecuteDeleteAsync(ct);
            await db.Messages.ExecuteDeleteAsync(ct);
            await db.CockcoinAwards.ExecuteDeleteAsync(ct);
            await db.TeamPeriodLineups.ExecuteDeleteAsync(ct);
            await db.Trades.ExecuteDeleteAsync(ct);
            await db.DraftPicks.ExecuteDeleteAsync(ct);
            await db.LeagueSeasons.ExecuteDeleteAsync(ct);
            await db.LeagueMembers.ExecuteDeleteAsync(ct);
            await db.Teams.ExecuteDeleteAsync(ct);
            await db.Leagues.ExecuteDeleteAsync(ct);
            await db.Users.ExecuteDeleteAsync(ct);
            // The simulation cursor belongs to a pool run, not to the NHL data.
            await db.SimulationState.ExecuteDeleteAsync(ct);

            // Week *boundaries* are calendar and survive; "this week is banked"
            // is pool state and must not. Leaving it set is silent and vicious:
            // the fresh league can never bank those weeks, so every team stays
            // at zero for them and nothing says why.
            await db.Periods
                .Where(p => p.FinalizedUtc != null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.FinalizedUtc, (DateTime?)null), ct);

            await tx.CommitAsync(ct);
        });

        Console.WriteLine("\nPool data wiped. NHL reference data untouched.");
        return 0;
    }
}
