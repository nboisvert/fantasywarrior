using FantasyWarrior.Core.Players;

namespace FantasyWarrior.Core.Tests.Players;

public class TimeOnIceTests
{
    [Theory]
    [InlineData("18:24", 1104)]
    [InlineData("0:00", 0)]
    // Minutes can exceed 59 in a long overtime — cannot be a TimeSpan parse.
    [InlineData("65:12", 3912)]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("garbage", 0)]
    public void ParseSeconds_HandlesValidAndInvalidInput(string? toi, int expected)
        => Assert.Equal(expected, TimeOnIce.ParseSeconds(toi));

    [Theory]
    [InlineData(1104, "18:24")]
    [InlineData(0, "0:00")]
    [InlineData(3912, "65:12")]
    [InlineData(65, "1:05")]
    public void Format_RoundTripsParseSeconds(int seconds, string expected)
        => Assert.Equal(expected, TimeOnIce.Format(seconds));

    [Fact]
    public void FormatAverage_AveragesValidLinesOnly()
    {
        // 18:00 (1080s), 20:00 (1200s), and a scratch ("0:00", excluded as
        // unplayed) and a null (missing) — average should be over the two
        // real games only, not diluted by the ones the player didn't play.
        var result = TimeOnIce.FormatAverage(["18:00", "20:00", "0:00", null]);

        Assert.Equal("19:00", result);
    }

    [Fact]
    public void FormatAverage_NullWhenNothingParseable()
    {
        var result = TimeOnIce.FormatAverage([null, "", "0:00"]);

        Assert.Null(result);
    }
}
