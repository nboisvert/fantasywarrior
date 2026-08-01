using FantasyWarrior.Jobs.CapWages;

namespace FantasyWarrior.Core.Tests.CapWages;

/// <summary>
/// The CapWages parser, run against pages actually captured from the live site
/// on 2026-08-01 (trimmed to three players to keep the fixtures readable, but
/// otherwise verbatim).
///
/// Real fixtures rather than handwritten JSON on purpose: the whole risk of a
/// scraper is that the source changes shape without warning. A fixture copied
/// from the site is the only kind that can be re-captured and diffed when a run
/// suddenly returns nothing.
/// </summary>
public class CapWagesParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string TeamPage() => Fixture("capwages-team-pit.html");

    private static string PlayerPage() => Fixture("capwages-player-crosby.html");

    // --- money ---

    [Theory]
    [InlineData("$8,700,000", 8_700_000L)]
    [InlineData("$780,000", 780_000L)]
    [InlineData("$0", 0L)]
    [InlineData("8700000", 8_700_000L)]
    public void ParseMoney_ReadsTheSitesFormatting(string raw, long expected) =>
        Assert.Equal(expected, CapWagesParser.ParseMoney(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    [InlineData("N/A")]
    public void ParseMoney_TreatsAnUnreadableValueAsUnknown_NotZero(string? raw)
    {
        // A missing salary read as 0 would silently under-report a roster's cap
        // total, which is worse than admitting we do not know.
        Assert.Null(CapWagesParser.ParseMoney(raw));
    }

    // --- season ---

    [Theory]
    [InlineData("2025-26", "20252026")]
    [InlineData("2005-06", "20052006")]
    public void ParseSeason_ConvertsToOurSeasonKey(string raw, string expected) =>
        Assert.Equal(expected, CapWagesParser.ParseSeason(raw));

    [Fact]
    public void ParseSeason_SurvivesTheCenturyRollover()
    {
        // Reading the trailing two digits would give "19990000" here. The end
        // year is computed from the start instead, which is why it does not.
        Assert.Equal("19992000", CapWagesParser.ParseSeason("1999-00"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1850-51")]
    public void ParseSeason_RejectsWhatIsNotASeason(string? raw) =>
        Assert.Null(CapWagesParser.ParseSeason(raw));

    // --- names ---

    [Theory]
    [InlineData("Crosby, Sidney", "Sidney Crosby")]
    [InlineData("Chinakhov, Yegor", "Yegor Chinakhov")]
    [InlineData("Sidney Crosby", "Sidney Crosby")]
    [InlineData("Ovechkin", "Ovechkin")]
    public void ToNaturalName_FlipsTheSitesLastFirstOrdering(string raw, string expected) =>
        Assert.Equal(expected, CapWagesParser.ToNaturalName(raw));

    // --- team page ---

    [Fact]
    public void ParseTeamPage_ReadsEveryPositionGroup()
    {
        var players = CapWagesParser.ParseTeamPage(TeamPage());

        Assert.Equal(3, players.Count);
        Assert.Contains(players, p => p.Slug == "sidney-crosby");
        Assert.All(players, p => Assert.Equal("PIT", p.TeamTricode));
    }

    [Fact]
    public void ParseTeamPage_ReadsTheSeasonBySeasonCapHits()
    {
        var crosby = CapWagesParser.ParseTeamPage(TeamPage()).Single(p => p.Slug == "sidney-crosby");

        var current = crosby.Seasons.Single(s => s.Season == "20252026");
        Assert.Equal(8_700_000, current.CapHit);
        Assert.Equal(8_700_000, current.Aav);
        Assert.Equal("NMC", current.Clause);
        Assert.Equal(780_000, current.BaseSalary);
        Assert.Equal(9_000_000, current.SigningBonuses);
    }

    [Fact]
    public void ParseTeamPage_ReturnsSeasonsInOrder_WithNoDuplicates()
    {
        var crosby = CapWagesParser.ParseTeamPage(TeamPage()).Single(p => p.Slug == "sidney-crosby");

        // An extension can overlap the deal it replaces, so the same season can
        // be listed twice. Two contract rows for one player-season would break
        // the (PlayerId, Season) unique index on the way in.
        Assert.Equal(crosby.Seasons.Select(s => s.Season).Distinct().Count(), crosby.Seasons.Count);
        Assert.Equal(crosby.Seasons.Select(s => s.Season).Order(), crosby.Seasons.Select(s => s.Season));
    }

    [Fact]
    public void ParseTeamPage_DoesNotFindNhlIds()
    {
        // Documents the reason player pages are fetched at all: the team page
        // has everything except the one field that would let us join without
        // matching on a name.
        var players = CapWagesParser.ParseTeamPage(TeamPage());

        Assert.All(players, p => Assert.Null(p.NhlId));
    }

    // --- player page ---

    [Fact]
    public void ParsePlayerPage_CarriesTheNhlId()
    {
        var crosby = CapWagesParser.ParsePlayerPage(PlayerPage());

        Assert.NotNull(crosby);
        // The whole point: a contract joins onto our Players table by this,
        // with no name matching anywhere.
        Assert.Equal(8471675, crosby!.NhlId);
        Assert.Equal("sidney-crosby", crosby.Slug);
    }

    [Fact]
    public void ParsePlayerPage_ReadsTheWholeCareer_NotJustTheCurrentDeal()
    {
        var crosby = CapWagesParser.ParsePlayerPage(PlayerPage())!;

        // Four contracts back to the 2005 entry-level deal.
        Assert.Contains(crosby.Seasons, s => s.Season == "20052006");
        Assert.Equal(850_000, crosby.Seasons.Single(s => s.Season == "20052006").CapHit);
        Assert.Equal(8_700_000, crosby.Seasons.Single(s => s.Season == "20252026").CapHit);
        Assert.True(crosby.Seasons.Count > 15);
    }

    // --- failure modes ---

    [Theory]
    [InlineData("<html><body>Not found</body></html>")]
    [InlineData("")]
    public void APageWithoutTheEmbeddedJson_YieldsNothing_RatherThanThrowing(string html)
    {
        // A 404, a redirect to a login wall, or a rewrite of the site all show
        // up this way. One bad page must not abort a sync of thirty-two.
        Assert.Empty(CapWagesParser.ParseTeamPage(html));
        Assert.Null(CapWagesParser.ParsePlayerPage(html));
    }

    [Fact]
    public void MalformedEmbeddedJson_YieldsNothing_RatherThanThrowing()
    {
        var html = """<script id="__NEXT_DATA__" type="application/json">{"props":</script>""";

        Assert.Empty(CapWagesParser.ParseTeamPage(html));
        Assert.Null(CapWagesParser.ParsePlayerPage(html));
    }

    [Fact]
    public void TeamSlugsAreDerivedFromOurOwnFranchiseNames()
    {
        // Verified against the live site on 2026-08-01: all five return 200.
        Assert.Equal("pittsburgh_penguins", CapWagesClient.SlugFor("Pittsburgh Penguins"));
        Assert.Equal("st_louis_blues", CapWagesClient.SlugFor("St. Louis Blues"));
        Assert.Equal("utah_mammoth", CapWagesClient.SlugFor("Utah Mammoth"));
        Assert.Equal("new_york_rangers", CapWagesClient.SlugFor("New York Rangers"));
        Assert.Equal("montreal_canadiens", CapWagesClient.SlugFor("Montreal Canadiens"));
    }
}
