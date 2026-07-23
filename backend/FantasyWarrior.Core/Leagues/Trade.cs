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
    /// <summary>The counterparty rejected the offer.</summary>
    public const string Declined = "declined";
    /// <summary>The proposer withdrew their own still-pending offer — distinct from Declined so the status alone says who acted.</summary>
    public const string Cancelled = "cancelled";
    public const string Accepted = "accepted";
    public const string Processed = "processed";
}

/// <summary>
/// One league member's "who won this trade" vote. Stored at
/// leagues/{leagueId}/trades/{tradeId}/votes/{username} — doc id is the
/// normalized username, so re-voting is a plain overwrite.
///
/// Stores the actual favored username (not a proposer/counterparty-relative
/// 1-5) so votes are meaningful on their own and can eventually roll up
/// across a GM's whole trade history (e.g. "how often does this GM come out
/// ahead in trades, per peers") without needing to know who was proposer vs
/// counterparty in each one — that cross-trade aggregate doesn't exist yet
/// this round, but the shape supports it.
/// </summary>
[FirestoreData]
public sealed class TradeVote
{
    /// <summary>Username judged to have won the trade; null = "fair".</summary>
    [FirestoreProperty("favoredUsername")]
    public string? FavoredUsername { get; set; }

    /// <summary>0 when FavoredUsername is null; 1 = "leans"; 2 = "clearly won".</summary>
    [FirestoreProperty("magnitude")]
    public int Magnitude { get; set; }

    [FirestoreProperty("votedUtc")]
    public Timestamp VotedUtc { get; set; }
}

/// <summary>Pure validation predicates, extracted for unit testing without Firestore.</summary>
public static class TradeValidation
{
    /// <summary>Only the receiving team can accept a still-pending trade.</summary>
    public static bool CanAccept(Trade trade, string normalizedUsername) =>
        trade.Status == TradeStatus.Pending && trade.CounterpartyUsername == normalizedUsername;

    /// <summary>Only the proposer can withdraw their own still-pending offer.</summary>
    public static bool CanCancel(Trade trade, string normalizedUsername) =>
        trade.Status == TradeStatus.Pending && trade.ProposerUsername == normalizedUsername;

    /// <summary>Only the receiving team can reject a still-pending offer.</summary>
    public static bool CanDecline(Trade trade, string normalizedUsername) =>
        trade.Status == TradeStatus.Pending && trade.CounterpartyUsername == normalizedUsername;

    /// <summary>A player can't appear on both sides of the same trade.</summary>
    public static bool HasOverlap(IReadOnlyCollection<long> playersFromProposer, IReadOnlyCollection<long> playersFromCounterparty) =>
        playersFromProposer.Intersect(playersFromCounterparty).Any();

    /// <summary>Valid combinations: (null, 0) = fair, or (proposer|counterparty, 1|2).</summary>
    public static bool IsValidVote(string? favoredUsername, int magnitude, string proposerUsername, string counterpartyUsername)
    {
        if (favoredUsername is null)
            return magnitude == 0;
        if (favoredUsername != proposerUsername && favoredUsername != counterpartyUsername)
            return false;
        return magnitude is 1 or 2;
    }

    /// <summary>Only a processed trade has a settled winner to rate.</summary>
    public static bool CanVoteOnTrade(Trade trade) => trade.Status == TradeStatus.Processed;

    /// <summary>A trade is visible to a viewer if it's public (accepted/processed) or they're one of the two parties.</summary>
    public static bool IsVisibleTo(Trade trade, string normalizedUsername) =>
        trade.Status is TradeStatus.Accepted or TradeStatus.Processed
        || trade.ProposerUsername == normalizedUsername
        || trade.CounterpartyUsername == normalizedUsername;
}
