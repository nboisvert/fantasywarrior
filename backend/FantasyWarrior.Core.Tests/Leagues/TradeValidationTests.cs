using FantasyWarrior.Core.Leagues;

namespace FantasyWarrior.Core.Tests.Leagues;

public class TradeValidationTests
{
    private static Trade PendingTrade() => new()
    {
        ProposerUsername = "jay",
        CounterpartyUsername = "nick",
        Status = TradeStatus.Pending,
    };

    [Fact]
    public void CanAccept_TrueOnlyForCounterpartyOnAPendingTrade()
    {
        var trade = PendingTrade();
        Assert.True(TradeValidation.CanAccept(trade, "nick"));
        Assert.False(TradeValidation.CanAccept(trade, "jay"));
        Assert.False(TradeValidation.CanAccept(trade, "someone-else"));
    }

    [Fact]
    public void CanAccept_FalseOnceNoLongerPending()
    {
        var trade = PendingTrade();
        trade.Status = TradeStatus.Accepted;
        Assert.False(TradeValidation.CanAccept(trade, "nick"));
    }

    [Fact]
    public void CanDecline_TrueOnlyForCounterpartyOnAPendingTrade()
    {
        // Rejecting someone else's offer — distinct from the proposer
        // withdrawing their own (CanCancel below).
        var trade = PendingTrade();
        Assert.True(TradeValidation.CanDecline(trade, "nick"));
        Assert.False(TradeValidation.CanDecline(trade, "jay"));
        Assert.False(TradeValidation.CanDecline(trade, "someone-else"));
    }

    [Fact]
    public void CanDecline_FalseOnceAlreadyResolved()
    {
        var trade = PendingTrade();
        trade.Status = TradeStatus.Declined;
        Assert.False(TradeValidation.CanDecline(trade, "nick"));
    }

    [Fact]
    public void CanCancel_TrueOnlyForProposerOnAPendingTrade()
    {
        // Withdrawing one's own offer — distinct from the counterparty
        // rejecting it (CanDecline above).
        var trade = PendingTrade();
        Assert.True(TradeValidation.CanCancel(trade, "jay"));
        Assert.False(TradeValidation.CanCancel(trade, "nick"));
        Assert.False(TradeValidation.CanCancel(trade, "someone-else"));
    }

    [Fact]
    public void CanCancel_FalseOnceAlreadyResolved()
    {
        var trade = PendingTrade();
        trade.Status = TradeStatus.Cancelled;
        Assert.False(TradeValidation.CanCancel(trade, "jay"));
    }

    [Fact]
    public void HasOverlap_TrueWhenAPlayerAppearsOnBothSides()
    {
        Assert.True(TradeValidation.HasOverlap([1, 2, 3], [3, 4]));
    }

    [Fact]
    public void HasOverlap_FalseWhenSidesAreDisjoint()
    {
        Assert.False(TradeValidation.HasOverlap([1, 2, 3], [4, 5]));
        Assert.False(TradeValidation.HasOverlap([], [1]));
        Assert.False(TradeValidation.HasOverlap([], []));
    }

    [Fact]
    public void IsValidVote_FairRequiresZeroMagnitude()
    {
        Assert.True(TradeValidation.IsValidVote(null, 0, "jay", "nick"));
        Assert.False(TradeValidation.IsValidVote(null, 1, "jay", "nick"));
        Assert.False(TradeValidation.IsValidVote(null, 2, "jay", "nick"));
    }

    [Fact]
    public void IsValidVote_FavoredMustBeOneOfTheTwoTeams()
    {
        Assert.True(TradeValidation.IsValidVote("jay", 1, "jay", "nick"));
        Assert.True(TradeValidation.IsValidVote("nick", 2, "jay", "nick"));
        Assert.False(TradeValidation.IsValidVote("someone-else", 1, "jay", "nick"));
    }

    [Fact]
    public void IsValidVote_MagnitudeMustBe1Or2WhenFavoredIsSet()
    {
        Assert.False(TradeValidation.IsValidVote("jay", 0, "jay", "nick"));
        Assert.False(TradeValidation.IsValidVote("jay", 3, "jay", "nick"));
    }

    [Fact]
    public void CanVoteOnTrade_TrueOnlyWhenProcessed()
    {
        var trade = PendingTrade();
        Assert.False(TradeValidation.CanVoteOnTrade(trade));
        trade.Status = TradeStatus.Processed;
        Assert.True(TradeValidation.CanVoteOnTrade(trade));
    }

    [Fact]
    public void IsVisibleTo_PendingOnlyVisibleToTheTwoParties()
    {
        var trade = PendingTrade();
        Assert.True(TradeValidation.IsVisibleTo(trade, "jay"));
        Assert.True(TradeValidation.IsVisibleTo(trade, "nick"));
        Assert.False(TradeValidation.IsVisibleTo(trade, "someone-else"));
    }

    [Fact]
    public void IsVisibleTo_DeclinedAndCancelledAreAlsoPrivate()
    {
        var trade = PendingTrade();
        trade.Status = TradeStatus.Declined;
        Assert.False(TradeValidation.IsVisibleTo(trade, "someone-else"));
        trade.Status = TradeStatus.Cancelled;
        Assert.False(TradeValidation.IsVisibleTo(trade, "someone-else"));
    }

    [Fact]
    public void IsVisibleTo_AcceptedAndProcessedAreVisibleToAnyone()
    {
        var trade = PendingTrade();
        trade.Status = TradeStatus.Accepted;
        Assert.True(TradeValidation.IsVisibleTo(trade, "someone-else"));
        trade.Status = TradeStatus.Processed;
        Assert.True(TradeValidation.IsVisibleTo(trade, "someone-else"));
    }
}
