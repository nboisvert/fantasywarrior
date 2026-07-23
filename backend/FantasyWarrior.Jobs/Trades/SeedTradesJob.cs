using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Players;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Trades;

/// <summary>
/// Wipes a league's `trades` collection (and each trade's `votes`
/// subcollection) and reseeds a fresh demo set spanning every status —
/// pending, accepted (not yet processed), declined, and processed — with
/// staggered historical timestamps so the Trades screen and the news
/// ticker's conditional display both have something real to show. The
/// `processed` trade is deliberately timestamped "now" (not backdated) so
/// it's still inside the news ticker's 30-minute hot-alert window right
/// after seeding.
/// </summary>
public sealed class SeedTradesJob(FirestoreDb db)
{
    public async Task RunAsync(string leagueId, CancellationToken ct = default)
    {
        var leagueRef = db.Collection("leagues").Document(leagueId);
        var leagueSnap = await leagueRef.GetSnapshotAsync(ct);
        if (!leagueSnap.Exists)
        {
            Console.Error.WriteLine("League not found.");
            return;
        }
        var league = leagueSnap.ConvertTo<League>();
        var tradesCol = leagueRef.Collection("trades");

        var existing = await tradesCol.GetSnapshotAsync(ct);
        foreach (var tradeDoc in existing.Documents)
        {
            var votes = await tradeDoc.Reference.Collection("votes").GetSnapshotAsync(ct);
            foreach (var v in votes.Documents)
                await v.Reference.DeleteAsync(cancellationToken: ct);
            await tradeDoc.Reference.DeleteAsync(cancellationToken: ct);
        }
        Console.WriteLine($"Wiped {existing.Count} existing trade(s).");

        var teamsSnap = await leagueRef.Collection("teams").GetSnapshotAsync(ct);
        var teams = teamsSnap.Documents.ToDictionary(d => d.Id, d => d.ConvertTo<Team>());

        long LastPlayerOf(string username) => teams[username].PlayerIds[^1];
        Timestamp Ago(int minutes) => Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(-minutes));

        // 1. Pending — proposed 10 minutes ago, nobody's responded yet.
        await tradesCol.AddAsync(new Trade
        {
            ProposerUsername = "sam",
            CounterpartyUsername = "vince",
            PlayersFromProposer = [LastPlayerOf("sam")],
            PlayersFromCounterparty = [LastPlayerOf("vince")],
            Status = TradeStatus.Pending,
            CreatedUtc = Ago(10),
        }, cancellationToken: ct);

        // 2. Accepted — proposed 2h ago, accepted 1h ago, still awaiting
        // tonight's process-trades run.
        await tradesCol.AddAsync(new Trade
        {
            ProposerUsername = "dom",
            CounterpartyUsername = "didi",
            PlayersFromProposer = [LastPlayerOf("dom")],
            PlayersFromCounterparty = [LastPlayerOf("didi")],
            Status = TradeStatus.Accepted,
            CreatedUtc = Ago(120),
            RespondedUtc = Ago(60),
        }, cancellationToken: ct);

        // 3. Declined — a day-old offer that got turned down.
        await tradesCol.AddAsync(new Trade
        {
            ProposerUsername = "baby",
            CounterpartyUsername = "jay",
            PlayersFromProposer = [LastPlayerOf("baby")],
            PlayersFromCounterparty = [LastPlayerOf("jay")],
            Status = TradeStatus.Declined,
            CreatedUtc = Ago(1440),
            RespondedUtc = Ago(1400),
        }, cancellationToken: ct);

        // 4. Processed & fresh — actually executes the roster swap via
        // RosterChange (not just a fake status flag) so the data stays
        // internally consistent, and stamps processedUtc as "now" so the
        // news ticker's 30-minute hot window is visibly active right after
        // seeding.
        var proposerUsername = "nick";
        var counterpartyUsername = "al";
        var proposerDoc = leagueRef.Collection("teams").Document(proposerUsername);
        var counterpartyDoc = leagueRef.Collection("teams").Document(counterpartyUsername);
        var proposerTeam = teams[proposerUsername];
        var counterpartyTeam = teams[counterpartyUsername];
        var proposerPlayer = LastPlayerOf(proposerUsername);
        var counterpartyPlayer = LastPlayerOf(counterpartyUsername);

        var allIds = proposerTeam.PlayerIds.Concat(counterpartyTeam.PlayerIds).Distinct().ToList();
        var positions = await FetchPositionsAsync(allIds, ct);
        var today = EtToday();
        var tradeRef = tradesCol.Document();

        await RosterChange.ApplyAsync(
            db, leagueRef, league, proposerDoc, proposerTeam, positions,
            playersOut: [proposerPlayer], playersIn: [counterpartyPlayer],
            adjustmentReason: "trade",
            creationEvent: AssignmentCreationEvent.Trade, creationEventReferenceId: tradeRef.Id,
            closeReason: AssignmentCloseReason.Trade, closeReasonReferenceId: tradeRef.Id,
            effectiveDate: today, ct);
        await RosterChange.ApplyAsync(
            db, leagueRef, league, counterpartyDoc, counterpartyTeam, positions,
            playersOut: [counterpartyPlayer], playersIn: [proposerPlayer],
            adjustmentReason: "trade",
            creationEvent: AssignmentCreationEvent.Trade, creationEventReferenceId: tradeRef.Id,
            closeReason: AssignmentCloseReason.Trade, closeReasonReferenceId: tradeRef.Id,
            effectiveDate: today, ct);

        await tradeRef.SetAsync(new Trade
        {
            ProposerUsername = proposerUsername,
            CounterpartyUsername = counterpartyUsername,
            PlayersFromProposer = [proposerPlayer],
            PlayersFromCounterparty = [counterpartyPlayer],
            Status = TradeStatus.Processed,
            CreatedUtc = Ago(50),
            RespondedUtc = Ago(45),
            ProcessedUtc = Timestamp.GetCurrentTimestamp(),
        }, cancellationToken: ct);

        Console.WriteLine("Seeded 4 trades: 1 pending (sam<->vince), 1 accepted (dom<->didi), " +
            "1 declined (baby<->jay), 1 processed just now (nick<->al) — still inside the 30-min hot window.");
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
