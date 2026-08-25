using FantasyWarrior.Core.Seasons;

namespace FantasyWarrior.Core.Tests.Seasons;

public class SeasonTests
{
    [Theory]
    [InlineData("20252026", true)]
    [InlineData("20262027", true)]
    [InlineData("2025-2026", false)]   // dashes: not a phantom season, just invalid
    [InlineData("20252025", false)]   // does not succeed itself
    [InlineData("20252027", false)]   // skips a year
    [InlineData("2025202", false)]    // wrong length
    [InlineData("", false)]
    public void IsValid_RequiresEightDigitsAndSuccession(string season, bool expected)
    {
        Assert.Equal(expected, Season.IsValid(season));
    }

    [Fact]
    public void StartYear_ReadsTheFirstFourDigits()
    {
        Assert.Equal(2026, Season.StartYear("20262027"));
    }

    [Fact]
    public void EndYear_IsStartYearPlusOne()
    {
        Assert.Equal(2027, Season.EndYear("20262027"));
    }

    [Fact]
    public void StartYear_ThrowsOnAMalformedSeason()
    {
        Assert.Throws<ArgumentException>(() => Season.StartYear("2025-2026"));
    }

    [Fact]
    public void FromStartYear_BuildsTheEightDigitForm()
    {
        Assert.Equal("20262027", Season.FromStartYear(2026));
    }

    [Fact]
    public void Next_AdvancesOneYear()
    {
        Assert.Equal("20262027", Season.Next("20252026"));
    }

    [Fact]
    public void Previous_IsTheInverseOfNext()
    {
        var season = "20262027";
        Assert.Equal(season, Season.Next(Season.Previous(season)));
    }

    [Theory]
    [InlineData(2026, 9, 1, "20262027")]   // September already belongs to the fall season
    [InlineData(2026, 10, 7, "20262027")]  // opening night
    [InlineData(2027, 4, 16, "20262027")]  // still the same season in April
    [InlineData(2027, 8, 31, "20262027")]  // last day of August, still last season
    public void CurrentOn_CutsOverInSeptember(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, Season.CurrentOn(new DateOnly(year, month, day)));
    }

    [Fact]
    public void Display_SplitsIntoTheFourDashTwoForm()
    {
        Assert.Equal("2026-27", Season.Display("20262027"));
    }

    [Fact]
    public void Display_ReturnsTheInputUnchangedWhenNotAValidSeason()
    {
        Assert.Equal("garbage", Season.Display("garbage"));
    }
}
