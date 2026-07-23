using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Tests.Leagues;

public class RosterChangeTests
{
    private static readonly Timestamp FixedNow = Timestamp.FromDateTime(new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void BuildOpenedAssignments_OneAssignmentPerIncomingPlayer_WithTradeSourceRefId()
    {
        var assignments = RosterChange.BuildOpenedAssignments(
            playersIn: [111, 222],
            teamUsername: "al",
            source: "trade",
            sourceRefId: "trade-abc",
            effectiveDate: "2026-07-24",
            createdUtc: FixedNow);

        Assert.Equal(2, assignments.Count);
        foreach (var a in assignments)
        {
            Assert.Equal("al", a.TeamUsername);
            Assert.Equal("2026-07-24", a.From);
            Assert.Null(a.To);
            Assert.Equal("trade", a.Source);
            Assert.Equal("trade-abc", a.SourceRefId);
            Assert.Equal(FixedNow, a.CreatedUtc);
        }
        Assert.Equal([111, 222], assignments.Select(a => a.PlayerId).ToList());
    }

    [Fact]
    public void BuildOpenedAssignments_EmptyWhenNoIncomingPlayers()
    {
        var assignments = RosterChange.BuildOpenedAssignments(
            playersIn: [], teamUsername: "al", source: "free_agency", sourceRefId: null,
            effectiveDate: "2026-07-24", createdUtc: FixedNow);

        Assert.Empty(assignments);
    }

    [Fact]
    public void BuildOpenedAssignments_FreeAgencyHasNoSourceRefId()
    {
        var assignments = RosterChange.BuildOpenedAssignments(
            playersIn: [111], teamUsername: "al", source: "free_agency", sourceRefId: null,
            effectiveDate: "2026-07-24", createdUtc: FixedNow);

        Assert.Null(Assert.Single(assignments).SourceRefId);
    }

    [Fact]
    public void BuildClosedAssignmentFields_SetsToAndClosedUtc_OnTopOfFrozenStats()
    {
        var totals = new PlayerRawTotals(GamesPlayed: 10, Goals: 4, Assists: 6);
        var fields = RosterChange.BuildClosedAssignmentFields(totals, finalFantasyPoints: 10, effectiveDate: "2026-07-24", closedUtc: FixedNow);

        Assert.Equal("2026-07-24", fields["to"]);
        Assert.Equal(FixedNow, fields["closedUtc"]);
        // Frozen stat fields come from AssignmentStats.ToFieldMap — spot-check a couple.
        Assert.Equal(10, fields["gamesPlayed"]);
        Assert.Equal(4, fields["goals"]);
        Assert.Equal(10.0, fields["fantasyPoints"]);
    }

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
