using FantasyWarrior.Core.Players;

namespace FantasyWarrior.Core.Tests.Players;

public class NotableLeaguesTests
{
    [Theory]
    [InlineData("NHL")]
    [InlineData("ahl")] // case-insensitive
    [InlineData("QMJHL")]
    [InlineData("KHL")]
    public void IsNotable_TrueForRecognisedLeagues(string league)
        => Assert.True(NotableLeagues.IsNotable(league));

    [Theory]
    [InlineData("GTHL")]
    [InlineData("QC Int PW")]
    [InlineData("NAPHL 14U")]
    [InlineData("Brick Invitational")]
    [InlineData("")]
    public void IsNotable_FalseForMinorOrYouthLeagues(string league)
        => Assert.False(NotableLeagues.IsNotable(league));

    [Fact]
    public void IsNotable_FalseForNull()
        => Assert.False(NotableLeagues.IsNotable(null));
}
