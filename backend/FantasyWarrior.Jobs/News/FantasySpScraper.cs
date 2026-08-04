using System.Net;
using HtmlAgilityPack;

namespace FantasyWarrior.Jobs.News;

/// <summary>
/// FantasySP publishes no public RSS feed (confirmed live: the guessed
/// /rss/nhl.xml path 404s) — the only way to get its NHL items is scraping
/// the server-rendered injuries table at fantasysp.com/injuries/nhl.
///
/// The page is one table per team, columns <c>#, Player, Team, Pos, Injury,
/// News</c> — confirmed against a live fetch on 2026-08-04 and captured as
/// <c>Fixtures/fantasysp-injuries.html</c>, which is what
/// <c>FantasySpScraperTests</c> runs against. The team name sits in an
/// <c>h6</c>, not the <c>h5</c> the vendor guide guessed, and it is not used:
/// we identify players by name, and the team we already hold is better than
/// the one a news page happens to print.
///
/// Personal/non-commercial use only per the site's terms; the shared
/// HttpClient carries an identifying User-Agent (see Jobs/Program.cs). This
/// job runs once a day, well within any reasonable rate limit for a single
/// page fetch.
///
/// The scraped table has no per-item date, unlike RSS — see NewsSyncJob's
/// "reliable published date" handling, which keeps a scraped item's
/// original PublishedUtc across reruns instead of stamping "now" every time.
/// </summary>
public sealed class FantasySpScraper(HttpClient http)
{
    public async Task<IReadOnlyList<NewsFeedItem>> GetInjuryItemsAsync(string pageUrl, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(pageUrl, ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"    ! {pageUrl} -> {(int)response.StatusCode} {response.StatusCode} (content-type: {contentType})");
                return [];
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            var items = Parse(html, pageUrl);

            if (items.Count == 0)
            {
                var snippet = html.Length > 200 ? html[..200] : html;
                Console.WriteLine($"    ! {pageUrl} -> 200 OK (content-type: {contentType}), but found 0 injury rows — page layout may have changed | body starts: {snippet}");
            }
            return items;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ! {pageUrl} -> request failed: {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// The parse, split from the fetch so it can be run against a captured
    /// page in tests — the whole risk of a scraper is the source changing
    /// shape, and this is the half that breaks when it does.
    /// </summary>
    public static IReadOnlyList<NewsFeedItem> Parse(string html, string pageUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var items = new List<NewsFeedItem>();
        var fetchedNow = DateTimeOffset.UtcNow;

        foreach (var row in doc.DocumentNode.SelectNodes("//table//tr") ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = row.SelectNodes("./td");
            // Six columns or it is not the injuries table. Reading a shorter
            // row would silently shift every field left by one, which is
            // exactly the bug this count guards against — the position column
            // spent a week being stored as the injury.
            if (cells is null || cells.Count < 6)
                continue;

            // The link is required, not just the text: it is both the proof
            // this is a player row and the stable key below. Any other
            // six-column table on the page has no player link in its second
            // cell and is skipped here.
            var playerLink = cells[1].SelectSingleNode(".//a[@href]");
            if (playerLink is null)
                continue;
            var player = Text(playerLink);
            if (string.IsNullOrEmpty(player))
                continue;

            var href = playerLink.GetAttributeValue("href", "");
            var url = new Uri(new Uri(pageUrl), href).ToString();
            var injury = Text(cells[4]);
            var news = Text(cells[5]);

            items.Add(new NewsFeedItem(
                // One row per player, keyed by his own page rather than by the
                // text: this source states a *current* condition, so tomorrow's
                // reworded sentence about the same knee is the same item, not
                // a second one.
                ExternalId: href,
                Headline: string.IsNullOrEmpty(injury) ? player : $"{player}: {injury}",
                Url: url,
                PlayerNameHint: player,
                PublishedUtc: fetchedNow,
                Body: string.IsNullOrEmpty(news) ? null : news,
                InjuryType: string.IsNullOrEmpty(injury) ? null : injury));
        }

        return items;
    }

    private static string Text(HtmlNode node) => WebUtility.HtmlDecode(node.InnerText).Trim();
}
