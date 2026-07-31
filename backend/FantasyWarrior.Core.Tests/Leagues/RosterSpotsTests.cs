using FantasyWarrior.Core.Leagues;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Tests.Leagues;

public class RosterSpotsTests
{
    private static readonly Timestamp Now = Timestamp.FromDateTime(new DateTime(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc));

    private static readonly Dictionary<long, string> Positions = new()
    {
        [8478402] = "C",  // forward
        [8477934] = "L",  // forward
        [8480069] = "D",
        [8476945] = "G",
    };

    [Fact]
    public void BuildOpened_OpensOneSpotPerIncomingPlayer()
    {
        var spots = RosterSpots.BuildOpened(
            [8478402, 8480069], "nick", Positions, RosterSpotReason.Trade, "trade-1", "2026-01-05", Now);

        Assert.Equal(2, spots.Count);
        Assert.All(spots, s =>
        {
            Assert.Equal("nick", s.TeamUsername);
            Assert.Equal("2026-01-05", s.StartDate);
            Assert.Equal(RosterSpotReason.Trade, s.StartReason);
            Assert.Equal("trade-1", s.StartRefId);
            Assert.Null(s.EndDate);
            Assert.True(s.IsOpen);
        });
    }

    [Fact]
    public void BuildOpened_DenormalizesThePositionGroup()
    {
        var spots = RosterSpots.BuildOpened(
            [8478402, 8477934, 8480069, 8476945], "nick", Positions,
            RosterSpotReason.Draft, null, "2026-01-05", Now);

        // Centres and wingers both collapse to F; lineup validation needs no join.
        Assert.Equal("F", spots[0].PositionGroup);
        Assert.Equal("F", spots[1].PositionGroup);
        Assert.Equal("D", spots[2].PositionGroup);
        Assert.Equal("G", spots[3].PositionGroup);
    }

    [Fact]
    public void BuildOpened_FallsBackToForwardForAnUnknownPlayer()
    {
        var spots = RosterSpots.BuildOpened([999], "nick", Positions, RosterSpotReason.FreeAgent, null, "2026-01-05", Now);

        Assert.Equal("C", spots[0].Position);
        Assert.Equal("F", spots[0].PositionGroup);
    }

    [Fact]
    public void BuildOpened_OfNobodyOpensNothing()
    {
        Assert.Empty(RosterSpots.BuildOpened([], "nick", Positions, RosterSpotReason.FreeAgent, null, "2026-01-05", Now));
    }

    [Fact]
    public void BuildClosedFields_RecordsWhyItEnded()
    {
        var fields = RosterSpots.BuildClosedFields("2026-01-07", Now, RosterSpotReason.Trade, "trade-9");

        Assert.Equal("2026-01-07", fields["endDate"]);
        Assert.Equal(Now, fields["closedUtc"]);
        Assert.Equal(RosterSpotReason.Trade, fields["endReason"]);
        Assert.Equal("trade-9", fields["endRefId"]);
    }

    [Fact]
    public void BuildClosedFields_OmitsAMissingReferenceRatherThanWritingNull()
    {
        var fields = RosterSpots.BuildClosedFields("2026-01-07", Now, RosterSpotReason.Release, null);

        Assert.False(fields.ContainsKey("endRefId"));
        Assert.Equal(RosterSpotReason.Release, fields["endReason"]);
    }

    [Fact]
    public void BuildClosedFields_WritesNoStats()
    {
        // Unlike the legacy Assignment, a spot's totals are a rollup of its
        // weekly lineups -- the scoring job owns them, so there is nothing to
        // freeze at close time.
        var fields = RosterSpots.BuildClosedFields("2026-01-07", Now, RosterSpotReason.Trade, null);

        Assert.Equal(3, fields.Count);
        Assert.False(fields.Keys.Any(k => k.Contains("oints") || k.Contains("tats")));
    }

    [Fact]
    public void ClosingDoesNotEraseWhyTheSpotOpened()
    {
        // A free-agent pickup later traded away must read as freeagent in and
        // trade out -- the activity feed depends on the two being separate.
        var spot = RosterSpots.BuildOpened([8478402], "nick", Positions, RosterSpotReason.FreeAgent, null, "2026-01-05", Now)[0];
        var closed = RosterSpots.BuildClosedFields("2026-01-07", Now, RosterSpotReason.Trade, "trade-3");

        Assert.Equal(RosterSpotReason.FreeAgent, spot.StartReason);
        Assert.Equal(RosterSpotReason.Trade, closed["endReason"]);
    }
}
