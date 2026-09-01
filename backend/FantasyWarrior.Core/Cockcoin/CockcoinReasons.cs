namespace FantasyWarrior.Core.Cockcoin;

/// <summary>
/// Short, stable codes for what earned a <see cref="Data.Entities.CockcoinAward"/>
/// — never user-supplied, so unlike <c>StatKeys</c> this isn't a validated
/// whitelist, just a place to keep the strings from drifting as more ways to
/// earn cockcoin get added (cockman-concept.md's "library of bonus-entry
/// prompt types" is meant to grow here).
/// </summary>
public static class CockcoinReasons
{
    public const string TradeVote = "trade-vote";

    /// <summary>Cockcoin awarded for <see cref="TradeVote"/>.</summary>
    public const int TradeVoteAmount = 2;

    /// <summary>A chat message landing on a <see cref="FibonacciMilestones"/>
    /// count within its (league, sender, recipient) room. No fixed amount —
    /// see <see cref="FibonacciMilestones.RewardForCount"/>.</summary>
    public const string ChatMessageMilestone = "chat-message-milestone";

    /// <summary>A trade offer landing on a <see cref="FibonacciMilestones"/>
    /// count within its (league, proposer, counterparty) pairing. No fixed
    /// amount — see <see cref="FibonacciMilestones.RewardForCount"/>.</summary>
    public const string TradeOfferMilestone = "trade-offer-milestone";

    /// <summary>The symmetric milestone for accepting: the Nth trade this
    /// counterparty has accepted from this specific proposer, same
    /// (league, proposer, counterparty) pairing and curve as
    /// <see cref="TradeOfferMilestone"/>, earned by the acceptor instead of
    /// the proposer.</summary>
    public const string TradeOfferAccepted = "trade-offer-accepted-milestone";

    /// <summary>A trade reaching <c>TradeStatus.Processed</c> — awarded to
    /// both GMs.</summary>
    public const string DoneDeal = "done-deal";

    /// <summary>Cockcoin awarded for <see cref="DoneDeal"/>, per GM.</summary>
    public const int DoneDealAmount = 10;

    /// <summary>Answering a Cockman campaign's question. Amount comes from
    /// the campaign's own <c>RewardAmount</c>, not a fixed constant.</summary>
    public const string CockmanCampaign = "cockman-campaign";
}
