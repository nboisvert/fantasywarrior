using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// A proposed player swap between two teams in a league. Stored at
/// leagues/{leagueId}/trades/{auto-id}. Proposing/accepting/declining never
/// touches rosters or scores by itself — only the nightly `process-trades`
/// job executes an `accepted` trade (via <see cref="RosterChange"/>), the
/// day after acceptance, so today's score is always computed on today's
/// rosters before any accepted trade takes effect.
/// </summary>
[FirestoreData]
public sealed class Trade
{
    [FirestoreProperty("proposerUsername")]
    public string ProposerUsername { get; set; } = "";

    [FirestoreProperty("counterpartyUsername")]
    public string CounterpartyUsername { get; set; } = "";

    /// <summary>Players the proposer gives up (go to the counterparty).</summary>
    [FirestoreProperty("playersFromProposer")]
    public List<long> PlayersFromProposer { get; set; } = [];

    /// <summary>Players the counterparty gives up (go to the proposer).</summary>
    [FirestoreProperty("playersFromCounterparty")]
    public List<long> PlayersFromCounterparty { get; set; } = [];

    [FirestoreProperty("status")]
    public string Status { get; set; } = TradeStatus.Pending;

    [FirestoreProperty("createdUtc")]
    public Timestamp CreatedUtc { get; set; }

    [FirestoreProperty("respondedUtc")]
    public Timestamp? RespondedUtc { get; set; }

    [FirestoreProperty("processedUtc")]
    public Timestamp? ProcessedUtc { get; set; }
}

public static class TradeStatus
{
    public const string Pending = "pending";
    public const string Declined = "declined";
    public const string Accepted = "accepted";
    public const string Processed = "processed";
}

/// <summary>
/// One league member's "who won this trade" vote, 1-5 (1 = proposer's team
/// clearly won, 3 = fair, 5 = counterparty's team clearly won). Stored at
/// leagues/{leagueId}/trades/{tradeId}/votes/{username} — doc id is the
/// normalized username, so re-voting is a plain overwrite.
/// </summary>
[FirestoreData]
public sealed class TradeVote
{
    [FirestoreProperty("level")]
    public int Level { get; set; }

    [FirestoreProperty("votedUtc")]
    public Timestamp VotedUtc { get; set; }
}

/// <summary>Pure validation predicates, extracted for unit testing without Firestore.</summary>
public static class TradeValidation
{
    /// <summary>Only the receiving team can accept a still-pending trade.</summary>
    public static bool CanAccept(Trade trade, string normalizedUsername) =>
        trade.Status == TradeStatus.Pending && trade.CounterpartyUsername == normalizedUsername;

    /// <summary>Either side can decline: the counterparty rejects the offer, or the proposer withdraws it.</summary>
    public static bool CanDecline(Trade trade, string normalizedUsername) =>
        trade.Status == TradeStatus.Pending &&
        (trade.CounterpartyUsername == normalizedUsername || trade.ProposerUsername == normalizedUsername);

    /// <summary>A player can't appear on both sides of the same trade.</summary>
    public static bool HasOverlap(IReadOnlyCollection<long> playersFromProposer, IReadOnlyCollection<long> playersFromCounterparty) =>
        playersFromProposer.Intersect(playersFromCounterparty).Any();
}
