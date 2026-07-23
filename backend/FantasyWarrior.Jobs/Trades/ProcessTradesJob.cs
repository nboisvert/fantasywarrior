using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Players;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Trades;

/// <summary>
/// Executes every `accepted` trade across all leagues: swaps the players via
/// <see cref="RosterChange.ApplyAsync"/> (once per team) and marks the trade
/// `processed`. Runs nightly, right after score-calc, so today's score is
/// always computed on today's rosters before any accepted trade takes
/// effect (from tomorrow).
/// </summary>
public sealed class ProcessTradesJob(FirestoreDb db)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var leagues = (await db.Collection("leagues").GetSnapshotAsync(ct)).Documents;
        var today = EtToday();

        foreach (var leagueSnap in leagues.Where(l => l.Exists))
        {
            var league = leagueSnap.ConvertTo<League>();
            var acceptedSnap = await leagueSnap.Reference.Collection("trades")
                .WhereEqualTo("status", TradeStatus.Accepted)
                .GetSnapshotAsync(ct);
            if (acceptedSnap.Count == 0)
                continue;

            foreach (var tradeDoc in acceptedSnap.Documents)
            {
                var trade = tradeDoc.ConvertTo<Trade>();
                try
                {
                    await ProcessOneAsync(leagueSnap.Reference, league, tradeDoc.Reference, trade, today, ct);
                    Console.WriteLine($"  processed trade {tradeDoc.Id} in '{league.Name}': {trade.ProposerUsername} <-> {trade.CounterpartyUsername}");
                }
                catch (Exception ex)
                {
                    // A tiny buddies'-pool scale accepted edge case: if a player
                    // involved got moved by some other concurrent transaction
                    // between acceptance and processing, skip this trade rather
                    // than aborting the whole nightly batch.
                    Console.Error.WriteLine($"  FAILED trade {tradeDoc.Id} in league {leagueSnap.Id}: {ex.Message}");
                }
            }
        }
    }

    private async Task ProcessOneAsync(
        DocumentReference leagueRef, League league, DocumentReference tradeRef, Trade trade, string today, CancellationToken ct)
    {
        var teamsCol = leagueRef.Collection("teams");
        var proposerDoc = teamsCol.Document(trade.ProposerUsername);
        var counterpartyDoc = teamsCol.Document(trade.CounterpartyUsername);
        var proposerTeam = (await proposerDoc.GetSnapshotAsync(ct)).ConvertTo<Team>();
        var counterpartyTeam = (await counterpartyDoc.GetSnapshotAsync(ct)).ConvertTo<Team>();

        var allIds = proposerTeam.PlayerIds.Concat(counterpartyTeam.PlayerIds).Distinct().ToList();
        var positions = await FetchPositionsAsync(allIds, ct);

        await RosterChange.ApplyAsync(
            db, leagueRef, league, proposerDoc, proposerTeam, positions,
            playersOut: trade.PlayersFromProposer, playersIn: trade.PlayersFromCounterparty,
            reason: "trade", source: "trade", sourceRefId: tradeRef.Id, effectiveDate: today, ct);

        await RosterChange.ApplyAsync(
            db, leagueRef, league, counterpartyDoc, counterpartyTeam, positions,
            playersOut: trade.PlayersFromCounterparty, playersIn: trade.PlayersFromProposer,
            reason: "trade", source: "trade", sourceRefId: tradeRef.Id, effectiveDate: today, ct);

        await tradeRef.UpdateAsync(new Dictionary<string, object>
        {
            ["status"] = TradeStatus.Processed,
            ["processedUtc"] = Timestamp.GetCurrentTimestamp(),
        }, cancellationToken: ct);
    }

    private async Task<Dictionary<long, string>> FetchPositionsAsync(IReadOnlyCollection<long> playerIds, CancellationToken ct)
    {
        if (playerIds.Count == 0)
            return [];
        var refs = playerIds.Select(id => db.Collection("players").Document(id.ToString())).ToArray();
        var snaps = await db.GetAllSnapshotsAsync(refs, ct);
        return snaps
            .Where(s => s.Exists)
            .ToDictionary(s => long.Parse(s.Id), s => s.ConvertTo<Player>().Position);
    }

    private static string EtToday()
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).ToString("yyyy-MM-dd");
    }
}
