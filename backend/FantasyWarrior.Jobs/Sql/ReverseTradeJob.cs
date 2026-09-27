using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Undoes one accepted-or-processed trade: closes the spots it opened, reopens
/// the spots it closed, deletes the trade itself. A one-off correction for a
/// trade a GM wants taken back before it plays out — not a feature the app
/// exposes, because <see cref="TradeExecution"/> deliberately runs at
/// acceptance rather than waiting, and nothing downstream expects a trade to
/// un-happen.
///
/// Refuses outright if any <see cref="RosterAssignment"/> on a spot this trade
/// opened is finalized — a banked week is the one thing scoring-model.md says a
/// trade can never move, and reversing one would move it.
/// </summary>
public sealed class ReverseTradeJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(string leagueCode, int tradeId, bool dryRun, CancellationToken ct = default)
    {
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.JoinCode == leagueCode, ct);
        if (league is null) { Console.Error.WriteLine($"No league with join code {leagueCode}."); return 1; }

        var trade = await db.Trades
            .Include(t => t.Assets)
            .Include(t => t.ProposerTeam).ThenInclude(t => t!.Owner)
            .Include(t => t.CounterpartyTeam).ThenInclude(t => t!.Owner)
            .Include(t => t.Votes)
            .FirstOrDefaultAsync(t => t.TradeId == tradeId && t.LeagueId == league.LeagueId, ct);
        if (trade is null) { Console.Error.WriteLine($"No trade {tradeId} in {league.Name}."); return 1; }

        if (trade.Status is TradeStatus.Pending or TradeStatus.Declined or TradeStatus.Cancelled)
        {
            Console.Error.WriteLine(
                $"Trade {tradeId} is {trade.Status} — nothing executed yet, so there is nothing to reverse. "
                + "Decline or cancel it from the app instead.");
            return 1;
        }

        Console.WriteLine($"=== reverse-trade{(dryRun ? "  [DRY RUN]" : "")}  {league.Name}, trade #{tradeId} ===");
        Console.WriteLine(
            $"{trade.ProposerTeam!.Owner!.Username} ({trade.ProposerTeam.Name}) <-> "
            + $"{trade.CounterpartyTeam!.Owner!.Username} ({trade.CounterpartyTeam.Name}), status {trade.Status}\n");

        var opened = await db.RosterSpots.Where(s => s.StartTradeId == tradeId).ToListAsync(ct);
        var closed = await db.RosterSpots.Where(s => s.EndTradeId == tradeId).ToListAsync(ct);

        var openedAssignments = await db.RosterAssignments
            .Where(a => opened.Select(s => s.RosterSpotId).Contains(a.RosterSpotId))
            .Include(a => a.Period)
            .ToListAsync(ct);
        var finalized = openedAssignments.Where(a => a.IsFinalized).ToList();
        if (finalized.Count > 0)
        {
            Console.Error.WriteLine(
                $"Refusing: {finalized.Count} banked week(s) already scored on a spot this trade opened "
                + $"(e.g. spot {finalized[0].RosterSpotId}, period {finalized[0].Period?.Number}). "
                + "A trade can never move history — see scoring-model.md.");
            return 1;
        }

        Console.WriteLine("Reopening (this trade closed these):");
        foreach (var s in closed)
            Console.WriteLine($"  spot {s.RosterSpotId,-6} team {s.TeamId,-6} {s.PositionGroup} "
                + $"player {s.PlayerId?.ToString() ?? s.FranchiseAbbrev} — was closed {s.EndDate}");

        Console.WriteLine("Deleting (this trade opened these):");
        foreach (var s in opened)
            Console.WriteLine($"  spot {s.RosterSpotId,-6} team {s.TeamId,-6} {s.PositionGroup} "
                + $"player {s.PlayerId?.ToString() ?? s.FranchiseAbbrev} — opened {s.StartDate}, "
                + $"{openedAssignments.Count(a => a.RosterSpotId == s.RosterSpotId)} lineup row(s) with it");

        var pickAssets = trade.Assets.Where(a => a.AssetType == TradeAssetType.DraftPick).ToList();
        var picks = pickAssets.Count == 0
            ? []
            : await db.DraftPicks
                .Where(p => pickAssets.Select(a => a.DraftPickId).Contains(p.DraftPickId))
                .ToListAsync(ct);
        foreach (var asset in pickAssets)
        {
            var pick = picks.First(p => p.DraftPickId == asset.DraftPickId);
            if (pick.CurrentTeamId != asset.ToTeamId)
            {
                Console.Error.WriteLine(
                    $"Refusing: pick {pick.DraftPickId} moved again since this trade "
                    + $"(now owned by team {pick.CurrentTeamId}, not {asset.ToTeamId}) — reverse that trade first.");
                return 1;
            }
            Console.WriteLine($"  pick {pick.DraftPickId} ({pick.Year} rd {pick.Round}): "
                + $"team {asset.ToTeamId} -> team {asset.FromTeamId}");
        }

        if (dryRun) { Console.WriteLine("\n[DRY RUN] Nothing written."); return 0; }

        // Two SaveChanges, one transaction: the delete has to land before the
        // reopen, or the (LeagueId, PlayerId) unique index rejects the reopened
        // spot while its still-open replacement is still on the table — EF
        // does not know the two rows are related and won't order for it.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            db.RosterAssignments.RemoveRange(openedAssignments);
            db.RosterSpots.RemoveRange(opened);
            await db.SaveChangesAsync(ct);

            foreach (var s in closed)
            {
                s.EndDate = null;
                s.EndReason = null;
                s.EndTradeId = null;
                s.ClosedUtc = null;
            }
            foreach (var asset in pickAssets)
                picks.First(p => p.DraftPickId == asset.DraftPickId).CurrentTeamId = asset.FromTeamId;

            db.TradeVotes.RemoveRange(trade.Votes);
            db.TradeAssets.RemoveRange(trade.Assets);
            db.Trades.Remove(trade);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        });

        Console.WriteLine("\nDone — trade deleted, rosters restored.");
        return 0;
    }
}
