using FantasyWarrior.Jobs.News;

namespace FantasyWarrior.Core.Tests.News;

/// <summary>
/// The Rotowire injuries parser, run against a page captured from the live
/// site on 2026-08-04 (trimmed to three blocks, otherwise verbatim). Same
/// reasoning as FantasySpScraperTests: a captured page is the only fixture
/// that can be re-captured and diffed the day a run returns nothing.
///
/// Three of the assertions here cover fields the first version of the scraper
/// dropped on the floor — the injury type, the factual paragraph, and the
/// per-item date it was documented as not having.
/// </summary>
public class RotowireInjuryScraperTests
{
    private const string PageUrl = "https://www.rotowire.com/hockey/news.php?view=injuries";
    private static readonly DateTimeOffset Fallback = new(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<NewsFeedItem> Parsed() => RotowireInjuryScraper.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rotowire-injuries.html")),
        PageUrl, Fallback);

    [Fact]
    public void Parse_ReadsEveryInjuryBlock()
    {
        var items = Parsed();
        Assert.Equal(3, items.Count);
        Assert.Equal(
            ["Caleb Desnoyers", "Isac Lundestrom", "Connor Bedard"],
            items.Select(i => i.PlayerNameHint));
    }

    [Fact]
    public void Parse_TakesTheInjuryFromTheSitesOwnField()
    {
        var lundestrom = Parsed().Single(i => i.PlayerNameHint == "Isac Lundestrom");
        Assert.Equal("Achilles", lundestrom.InjuryType);
    }

    /// <summary>
    /// Connor Bedard's block is headlined "Signs five-year contract" while its
    /// injury field says "Shoulder" — a contract note filed under a hurt
    /// player. Reading the injury out of the headline would have called him
    /// healthy, which is exactly why it is read from the field instead.
    /// </summary>
    [Fact]
    public void Parse_DoesNotInferTheInjuryFromTheHeadline()
    {
        var bedard = Parsed().Single(i => i.PlayerNameHint == "Connor Bedard");
        Assert.Equal("Connor Bedard: Signs five-year contract", bedard.Headline);
        Assert.Equal("Shoulder", bedard.InjuryType);
    }

    [Fact]
    public void Parse_KeepsTheFactualParagraphAsTheBody()
    {
        var lundestrom = Parsed().Single(i => i.PlayerNameHint == "Isac Lundestrom");
        Assert.NotNull(lundestrom.Body);
        Assert.Contains("Achilles surgery", lundestrom.Body);
    }

    /// <summary>The one thing the site's terms actually forbid storing.</summary>
    [Fact]
    public void Parse_NeverCapturesTheSubscriptionLockedAnalysis()
    {
        foreach (var item in Parsed())
        {
            Assert.DoesNotContain("ANALYSIS", item.Body ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Subscribe now", item.Body ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Parse_ReadsTheItemsOwnDate_NotTheDayItWasFetched()
    {
        var items = Parsed();
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), items[0].PublishedUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero), items[1].PublishedUtc);
        Assert.All(items, i => Assert.NotEqual(Fallback, i.PublishedUtc));
    }

    [Theory]
    [InlineData("August 1, 2026", 2026, 8, 1)]
    [InlineData("July 19, 2026", 2026, 7, 19)]
    [InlineData("Jul 19, 2026", 2026, 7, 19)]
    public void ParseTimestamp_ReadsTheSitesFormat(string raw, int year, int month, int day) =>
        Assert.Equal(
            new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero),
            RotowireInjuryScraper.ParseTimestamp(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2 hours ago")]
    public void ParseTimestamp_TreatsAnUnreadableDateAsAbsent(string? raw) =>
        Assert.Null(RotowireInjuryScraper.ParseTimestamp(raw));

    /// <summary>
    /// Synthetic, unlike the fixture: every block on the captured page carried
    /// <c>is-injured</c>, but the page has historically also shown trades and
    /// signings, and one of those must never mark a healthy player out.
    /// </summary>
    [Fact]
    public void Parse_SkipsABlockTheSiteDoesNotFlagAsAnInjury()
    {
        const string html = """
            <html><body>
              <div class="news-update">
                <a class="news-update__player-link" href="/hockey/player/sidney-crosby-2874">Sidney Crosby</a>
                <div class="news-update__headline">Dealt to Montreal</div>
              </div>
              <div class="news-update is-injured">
                <a class="news-update__player-link" href="/hockey/player/nathan-mackinnon-4256">Nathan MacKinnon</a>
                <div class="news-update__headline">Out week to week</div>
                <div class="news-update__inj">Knee</div>
              </div>
            </body></html>
            """;
        var items = RotowireInjuryScraper.Parse(html, PageUrl, Fallback);
        Assert.Equal("Nathan MacKinnon", Assert.Single(items).PlayerNameHint);
    }

    [Fact]
    public void Parse_FallsBackToTheRunsDateWhenABlockHasNoTimestamp()
    {
        const string html = """
            <html><body><div class="news-update is-injured">
              <a class="news-update__player-link" href="/hockey/player/nathan-mackinnon-4256">Nathan MacKinnon</a>
              <div class="news-update__inj">Knee</div>
            </div></body></html>
            """;
        Assert.Equal(Fallback, Assert.Single(RotowireInjuryScraper.Parse(html, PageUrl, Fallback)).PublishedUtc);
    }

    [Fact]
    public void Parse_ReturnsNothingForAPageItDoesNotRecognise()
    {
        Assert.Empty(RotowireInjuryScraper.Parse("<html><body><h1>Not found</h1></body></html>", PageUrl, Fallback));
    }
}
