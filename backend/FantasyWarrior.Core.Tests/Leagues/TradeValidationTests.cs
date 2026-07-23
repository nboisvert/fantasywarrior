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
    public void CanDecline_TrueForEitherSideOnAPendingTrade()
    {
        var trade = PendingTrade();
        // Counterparty declining the offer.
        Assert.True(TradeValidation.CanDecline(trade, "nick"));
        // Proposer withdrawing their own offer.
        Assert.True(TradeValidation.CanDecline(trade, "jay"));
        Assert.False(TradeValidation.CanDecline(trade, "someone-else"));
    }

    [Fact]
    public void CanDecline_FalseOnceAlreadyResolved()
    {
        var trade = PendingTrade();
        trade.Status = TradeStatus.Declined;
        Assert.False(TradeValidation.CanDecline(trade, "nick"));
        Assert.False(TradeValidation.CanDecline(trade, "jay"));
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
}
