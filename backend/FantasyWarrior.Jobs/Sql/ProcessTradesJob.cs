using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Rosters;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Executes accepted trades: closes the outgoing roster spots, opens the
/// incoming ones, and hands over any draft picks.
///
/// **Always effective at a week boundary**, never "today". A swap landing
/// mid-week would give a player two owners for one scoring window, which is the
/// one thing the banked-points model assumes cannot happen.
///
/// Accepting a trade deliberately does not execute it — that is why this job
/// exists at all. It runs from the nightly pass, only on a night a week
/// actually closed.
/// </summary>
public sealed class ProcessTradesJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(DateOnly effectiveDate, CancellationToken ct = default)
    {
        var accepted = await db.Trades
            .Where(t => t.Status == TradeStatus.Accepted)
            .Include(t => t.Assets)
            .ToListAsync(ct);

        if (accepted.Count == 0)
        {
            Console.WriteLine("  No accepted trades waiting.");
            return 0;
        }

        Console.WriteLine($"  {accepted.Count} trade(s), effective {effectiveDate:yyyy-MM-dd}");
        var now = DateTime.UtcNow;

        foreach (var trade in accepted)
        {
            // One transaction per trade: a half-applied swap is the one outcome
            // that leaves the league in a state no rule can describe. Opened
            // through the execution strategy because retries are enabled — the
            // serverless tier drops connections on resume, and a retry must
            // replay the whole swap rather than half of it.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var side in trade.Assets.GroupBy(a => a.FromTeamId))
                {
                    var players = side.Where(a => a.AssetType == TradeAssetType.Player)
                        .Select(a => a.PlayerId!.Value).ToList();
                    // A franchise moves through the same door: it is a roster
                    // spot closing on one team and opening on the other, and
                    // nothing else. The balance rule upstream guarantees the
                    // other side is sending one back, so neither team is ever
                    // left with two or none.
                    var franchises = side.Where(a => a.AssetType == TradeAssetType.Franchise)
                        .Select(a => a.FranchiseAbbrev!).ToList();
                    if (players.Count == 0 && franchises.Count == 0) continue;

                    var receivingTeam = side.First().ToTeamId;
                    await RosterChange.ApplyAsync(
                        db, trade.LeagueId, teamId: side.Key,
                        playersOut: players, playersIn: [],
                        startReason: RosterSpotStartReason.Trade,
                        endReason: RosterSpotEndReason.Trade,
                        effectiveDate: effectiveDate, tradeId: trade.TradeId,
                        franchisesOut: franchises, ct: ct);
                    await RosterChange.ApplyAsync(
                        db, trade.LeagueId, teamId: receivingTeam,
                        playersOut: [], playersIn: players,
                        startReason: RosterSpotStartReason.Trade,
                        endReason: RosterSpotEndReason.Trade,
                        effectiveDate: effectiveDate, tradeId: trade.TradeId,
                        franchisesIn: franchises, ct: ct);
                }

                // Picks change hands by pointing at their new owner. The
                // original owner is never touched — that column is what makes
                // "Pittsburgh's 2nd, via Boston" expressible at all.
                foreach (var asset in trade.Assets.Where(a => a.AssetType == TradeAssetType.DraftPick))
                {
                    var pick = await db.DraftPicks.FirstOrDefaultAsync(p => p.DraftPickId == asset.DraftPickId, ct);
                    if (pick is not null) pick.CurrentTeamId = asset.ToTeamId;
                }

                trade.Status = TradeStatus.Processed;
                trade.ProcessedUtc = now;
                trade.EffectiveDate = effectiveDate;

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                var playerCount = trade.Assets.Count(a => a.AssetType == TradeAssetType.Player);
                var pickCount = trade.Assets.Count(a => a.AssetType == TradeAssetType.DraftPick);
                var franchiseCount = trade.Assets.Count(a => a.AssetType == TradeAssetType.Franchise);
                Console.WriteLine($"    trade {trade.TradeId}: {playerCount} player(s), {pickCount} pick(s)"
                    + (franchiseCount == 0 ? "" : $", {franchiseCount} franchise(s)") + " — processed");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                // One bad trade must not stop the rest of the night.
                Console.Error.WriteLine($"    ! trade {trade.TradeId} failed, rolled back: {ex.Message}");
                db.ChangeTracker.Clear();
            }
            });
        }
        return 0;
    }
}
