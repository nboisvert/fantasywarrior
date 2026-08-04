using FantasyWarrior.Jobs.News;

namespace FantasyWarrior.Core.Tests.News;

/// <summary>
/// The FantasySP injuries parser, run against a page captured from the live
/// site on 2026-08-04 (trimmed to three teams to keep the fixture readable,
/// but otherwise verbatim).
///
/// A real fixture rather than handwritten HTML, for the same reason as
/// CapWagesParserTests: the whole risk of a scraper is the source changing
/// shape without warning, and only a captured page can be re-captured and
/// diffed when a run suddenly returns nothing.
///
/// This suite exists because of a specific bug. The columns are
/// <c>#, Player, Team, Pos, Injury, News</c>, and the first version read
/// index 3 and 4 — so every player's *position* was stored as his injury and
/// the injury as the news, producing headlines like "Troy Terry (RW): Hip"
/// in production. Nothing threw; the feature was simply wrong.
/// </summary>
public class FantasySpScraperTests
{
    private const string PageUrl = "https://www.fantasysp.com/injuries/nhl/";

    private static IReadOnlyList<NewsFeedItem> Parsed() => FantasySpScraper.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fantasysp-injuries.html")),
        PageUrl);

    [Fact]
    public void Parse_ReadsEveryPlayerRow()
    {
        var items = Parsed();
        Assert.Equal(3, items.Count);
        Assert.Equal(
            ["Troy Terry", "Charlie McAvoy", "Seth Jarvis"],
            items.Select(i => i.PlayerNameHint));
    }

    [Fact]
    public void Parse_TakesTheInjuryColumn_NotThePositionNextToIt()
    {
        var terry = Parsed().Single(i => i.PlayerNameHint == "Troy Terry");
        Assert.Equal("Hip", terry.InjuryType);
        Assert.DoesNotContain("RW", terry.Headline);
        Assert.Equal("Troy Terry: Hip", terry.Headline);
    }

    [Fact]
    public void Parse_KeepsTheNewsSentenceAsTheBody()
    {
        var jarvis = Parsed().Single(i => i.PlayerNameHint == "Seth Jarvis");
        Assert.NotNull(jarvis.Body);
        Assert.Contains("surgery on his shoulder", jarvis.Body, StringComparison.OrdinalIgnoreCase);
        // The body is the paragraph, so it must not have been folded into the
        // headline — that is what made the old headlines unreadable.
        Assert.True(jarvis.Headline.Length < 40, jarvis.Headline);
    }

    /// <summary>The key has to survive the site rewording tomorrow's sentence
    /// about the same knee, or one injury becomes a new row every night.</summary>
    [Fact]
    public void Parse_KeysAnItemByThePlayersOwnPage_NotItsText()
    {
        var mcavoy = Parsed().Single(i => i.PlayerNameHint == "Charlie McAvoy");
        Assert.Equal("/nhl_player_news/Charlie_McAvoy/", mcavoy.ExternalId);
        Assert.Equal("https://www.fantasysp.com/nhl_player_news/Charlie_McAvoy/", mcavoy.Url);
    }

    /// <summary>A suspension is filed in the Injury column next to real
    /// injuries, which is exactly why the kind is decided from the label
    /// rather than assumed.</summary>
    [Fact]
    public void Parse_ReportsASuspensionAsItIsWritten()
    {
        var mcavoy = Parsed().Single(i => i.PlayerNameHint == "Charlie McAvoy");
        Assert.Equal("Suspension", mcavoy.InjuryType);
    }

    [Fact]
    public void Parse_IgnoresTablesThatAreNotTheInjuriesTable()
    {
        // Six columns, no player link — a standings or schedule table would
        // otherwise walk straight into the roster as phantom injuries.
        const string html = """
            <html><body><table><tr>
              <td>1</td><td>Atlantic</td><td>82</td><td>50</td><td>25</td><td>7</td>
            </tr></table></body></html>
            """;
        Assert.Empty(FantasySpScraper.Parse(html, PageUrl));
    }

    [Fact]
    public void Parse_IgnoresAShortRowRatherThanShiftingEveryFieldLeft()
    {
        const string html = """
            <html><body><table><tr>
              <td>1</td><td><a href="/nhl_player_news/Troy_Terry/">Troy Terry</a></td>
              <td>ANA</td><td>RW</td><td>Hip</td>
            </tr></table></body></html>
            """;
        Assert.Empty(FantasySpScraper.Parse(html, PageUrl));
    }

    [Fact]
    public void Parse_ReturnsNothingForAPageItDoesNotRecognise()
    {
        Assert.Empty(FantasySpScraper.Parse("<html><body><h1>Not found</h1></body></html>", PageUrl));
    }
}
