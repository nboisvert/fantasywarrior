using FantasyWarrior.Core.Leagues;

namespace FantasyWarrior.Core.Tests.Leagues;

/// <summary>
/// What survives of RosterChange after banked points removed the adjustment
/// ledger. The spot builders it used to own now live in RosterSpots and are
/// tested there.
/// </summary>
public class RosterChangeTests
{
    [Fact]
    public void BuildNewPlayerIds_RemovesOutgoingAndAppendsIncoming()
    {
        var result = RosterChange.BuildNewPlayerIds(
            currentPlayerIds: [1, 2, 3], playersOut: [2], playersIn: [4]);

        Assert.Equal([1, 3, 4], result);
    }

    [Fact]
    public void BuildNewPlayerIds_HandlesAddOnly()
    {
        var result = RosterChange.BuildNewPlayerIds(
            currentPlayerIds: [1, 2], playersOut: [], playersIn: [3]);

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public void BuildNewPlayerIds_HandlesDropOnly()
    {
        var result = RosterChange.BuildNewPlayerIds(
            currentPlayerIds: [1, 2, 3], playersOut: [1, 3], playersIn: []);

        Assert.Equal([2], result);
    }

    [Fact]
    public void BuildNewPlayerIds_TradeSwapsBothSidesOfOneTeam()
    {
        // Mirrors what ProcessTradesJob does for each side of an accepted
        // trade: this team gives up its outgoing players and receives the
        // other team's players in the same call.
        var result = RosterChange.BuildNewPlayerIds(
            currentPlayerIds: [10, 20, 30], playersOut: [20], playersIn: [99]);

        Assert.Equal([10, 30, 99], result);
    }
}
