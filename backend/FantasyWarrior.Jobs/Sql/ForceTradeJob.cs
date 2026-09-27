using FantasyWarrior.Core.Periods;
using FantasyWarrior.Core.Trades;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Rosters;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Enters and executes one trade directly, skipping the cap/roster-size
/// validation <c>POST /trades</c> enforces — for a trade two GMs already agreed
/// to outside the app (chat, a league Facebook post) that the app's own limits
/// would otherwise refuse. Still checks that each side actually holds what it
/// is offering: a typo in a player id is a bug, not an agreed trade.
///
/// Effective the same way an in-app acceptance is: the start of the next
/// period, via <see cref="TradeSchedule"/>, so lineups and cap totals read
/// this exactly like any other trade from the moment it lands.
/// </summary>
public sealed class ForceTradeJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(
        string leagueCode, string proposerUsername, string counterpartyUsername,
        List<long> playersFromProposer, List<long> playersFromCounterparty,
        DateOnly today, bool dryRun, CancellationToken ct = default) =>
        await RunAsync(leagueCode, proposerUsername, counterpartyUsername,
            playersFromProposer, playersFromCounterparty, [], [], today, dryRun, ct);

    public async Task<int> RunAsync(
        string leagueCode, string proposerUsername, string counterpartyUsername,
        List<long> playersFromProposer, List<long> playersFromCounterparty,
        List<int> picksFromProposer, List<int> picksFromCounterparty,
        DateOnly today, bool dryRun, CancellationToken ct = default)
    {
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.JoinCode == leagueCode, ct);
        if (league is null) { Console.Error.WriteLine($"No league with join code {leagueCode}."); return 1; }

        var proposerTeam = await db.Teams.Include(t => t.Owner)
            .FirstOrDefaultAsync(t => t.LeagueId == league.LeagueId && t.Owner!.Username == proposerUsername, ct);
        var counterpartyTeam = await db.Teams.Include(t => t.Owner)
            .FirstOrDefaultAsync(t => t.LeagueId == league.LeagueId && t.Owner!.Username == counterpartyUsername, ct);
        if (proposerTeam is null || counterpartyTeam is null)
        {
            Console.Error.WriteLine("Team not found for one of the two usernames.");
            return 1;
        }

        var held = await db.RosterSpots
            .Where(s => s.LeagueId == league.LeagueId && s.EndDate == null && s.PlayerId != null)
            .Select(s => new { s.TeamId, s.PlayerId })
            .ToListAsync(ct);
        var proposerHas = held.Where(h => h.TeamId == proposerTeam.TeamId).Select(h => h.PlayerId!.Value).ToHashSet();
        var counterpartyHas = held.Where(h => h.TeamId == counterpartyTeam.TeamId).Select(h => h.PlayerId!.Value).ToHashSet();

        var badProposer = playersFromProposer.Where(id => !proposerHas.Contains(id)).ToList();
        var badCounterparty = playersFromCounterparty.Where(id => !counterpartyHas.Contains(id)).ToList();
        if (badProposer.Count > 0 || badCounterparty.Count > 0)
        {
            if (badProposer.Count > 0)
                Console.Error.WriteLine($"{proposerUsername} does not currently hold: {string.Join(", ", badProposer)}.");
            if (badCounterparty.Count > 0)
                Console.Error.WriteLine($"{counterpartyUsername} does not currently hold: {string.Join(", ", badCounterparty)}.");
            return 1;
        }

        var players = await db.Players
            .Where(p => playersFromProposer.Concat(playersFromCounterparty).Contains(p.PlayerId))
            .ToDictionaryAsync(p => p.PlayerId, p => p.FullName, ct);

        var pickIds = picksFromProposer.Concat(picksFromCounterparty).ToList();
        var picks = pickIds.Count == 0
            ? []
            : await db.DraftPicks.Where(p => pickIds.Contains(p.DraftPickId)).ToListAsync(ct);
        var badPicks = new List<string>();
        foreach (var (ids, teamId, whose) in new[]
                 { (picksFromProposer, proposerTeam.TeamId, proposerUsername), (picksFromCounterparty, counterpartyTeam.TeamId, counterpartyUsername) })
            foreach (var id in ids)
            {
                var pick = picks.FirstOrDefault(p => p.DraftPickId == id);
                if (pick is null || pick.CurrentTeamId != teamId || pick.UsedUtc is not null)
                    badPicks.Add($"pick {id} is not currently {whose}'s to trade");
            }
        if (badPicks.Count > 0)
        {
            foreach (var b in badPicks) Console.Error.WriteLine(b);
            return 1;
        }

        var periods = await db.Periods.Where(p => p.Season == league.Season)
            .Select(p => new PeriodSpan(p.Number, p.StartDate, p.EndDate)).ToListAsync(ct);
        var effectiveDate = TradeSchedule.NextPeriodStart(periods, today);
        if (effectiveDate is not { } effective)
        {
            Console.Error.WriteLine("The season has no week left for this trade to take effect in.");
            return 1;
        }
        var nextPeriod = await db.Periods.FirstAsync(p => p.Season == league.Season && p.StartDate == effective, ct);

        Console.WriteLine($"=== force-trade{(dryRun ? "  [DRY RUN]" : "")}  {league.Name} ===");
        Console.WriteLine($"{proposerUsername} ({proposerTeam.Name}) <-> {counterpartyUsername} ({counterpartyTeam.Name}), "
            + $"effective {effective:yyyy-MM-dd} (week {nextPeriod.Number})");
        string PickLabel(int id)
        {
            var p = picks.First(x => x.DraftPickId == id);
            return $"{p.Year} rd {p.Round} pick (#{id})";
        }
        Console.WriteLine($"  {proposerUsername} sends: {string.Join(", ", playersFromProposer.Select(id => players.GetValueOrDefault(id, id.ToString())).Concat(picksFromProposer.Select(PickLabel)))}");
        Console.WriteLine($"  {counterpartyUsername} sends: {string.Join(", ", playersFromCounterparty.Select(id => players.GetValueOrDefault(id, id.ToString())).Concat(picksFromCounterparty.Select(PickLabel)))}");
        Console.WriteLine("  (cap and roster-size limits NOT checked — bypassed by design)");

        if (dryRun) { Console.WriteLine("\n[DRY RUN] Nothing written."); return 0; }

        var trade = new Trade
        {
            LeagueId = league.LeagueId,
            ProposerTeamId = proposerTeam.TeamId,
            CounterpartyTeamId = counterpartyTeam.TeamId,
            Status = TradeStatus.Accepted,
            CreatedUtc = DateTime.UtcNow,
            RespondedUtc = DateTime.UtcNow,
            EffectiveDate = effective,
        };
        db.Trades.Add(trade);

        foreach (var id in playersFromProposer)
            db.TradeAssets.Add(new TradeAsset
            {
                Trade = trade, FromTeamId = proposerTeam.TeamId, ToTeamId = counterpartyTeam.TeamId,
                AssetType = TradeAssetType.Player, PlayerId = id,
            });
        foreach (var id in playersFromCounterparty)
            db.TradeAssets.Add(new TradeAsset
            {
                Trade = trade, FromTeamId = counterpartyTeam.TeamId, ToTeamId = proposerTeam.TeamId,
                AssetType = TradeAssetType.Player, PlayerId = id,
            });
        foreach (var id in picksFromProposer)
            db.TradeAssets.Add(new TradeAsset
            {
                Trade = trade, FromTeamId = proposerTeam.TeamId, ToTeamId = counterpartyTeam.TeamId,
                AssetType = TradeAssetType.DraftPick, DraftPickId = id,
            });
        foreach (var id in picksFromCounterparty)
            db.TradeAssets.Add(new TradeAsset
            {
                Trade = trade, FromTeamId = counterpartyTeam.TeamId, ToTeamId = proposerTeam.TeamId,
                AssetType = TradeAssetType.DraftPick, DraftPickId = id,
            });

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // trade.Assets is already populated by EF's navigation fixup —
            // every TradeAsset above was added in this same batch with its
            // Trade reference set, so there is nothing to load from the
            // database yet (TradeId is still 0 until SaveChanges below).
            await TradeExecution.ApplyAsync(db, trade, effective, nextPeriod, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        Console.WriteLine($"\nDone — trade #{trade.TradeId} entered and executed.");
        return 0;
    }
}
