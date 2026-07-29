using System.Net;
using HtmlAgilityPack;

namespace FantasyWarrior.Jobs.News;

/// <summary>
/// FantasySP publishes no public RSS feed (confirmed live: the guessed
/// /rss/nhl.xml path 404s) — the only way to get its NHL items is scraping
/// the server-rendered injuries table at fantasysp.com/injuries/nhl. Per
/// the integration guide, the page is organized as repeating h5 team
/// headers each followed by a table (columns: #, Player, Team, Pos, Injury,
/// News) — documented as unverified against live DOM (the site can
/// restructure this periodically), so a miss logs loudly rather than
/// failing silently, same convention as RssNewsClient.
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
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var items = new List<NewsFeedItem>();
            string? currentTeam = null;
            var fetchedNow = DateTimeOffset.UtcNow;
            var nodes = doc.DocumentNode.SelectNodes("//h5 | //table");
            foreach (var node in nodes ?? Enumerable.Empty<HtmlNode>())
            {
                if (node.Name == "h5")
                {
                    currentTeam = WebUtility.HtmlDecode(node.InnerText).Trim();
                    continue;
                }

                var rows = node.SelectNodes(".//tbody/tr") ?? node.SelectNodes(".//tr");
                if (rows is null)
                    continue;

                foreach (var row in rows)
                {
                    var cells = row.SelectNodes("./td");
                    if (cells is null || cells.Count < 5)
                        continue;

                    var playerLink = cells[1].SelectSingleNode(".//a");
                    var player = WebUtility.HtmlDecode((playerLink ?? cells[1]).InnerText).Trim();
                    if (string.IsNullOrEmpty(player))
                        continue;

                    var href = playerLink?.GetAttributeValue("href", null);
                    var url = href is null ? pageUrl : new Uri(new Uri(pageUrl), href).ToString();
                    var injury = WebUtility.HtmlDecode(cells[3].InnerText).Trim();
                    var news = WebUtility.HtmlDecode(cells[4].InnerText).Trim();
                    var headline = string.IsNullOrEmpty(injury) ? $"{player}: {news}" : $"{player} ({injury}): {news}";

                    items.Add(new NewsFeedItem(
                        ExternalId: $"{currentTeam}-{player}-{news}",
                        Headline: headline,
                        Url: url,
                        PlayerNameHint: player,
                        PublishedUtc: fetchedNow));
                }
            }

            if (items.Count == 0)
            {
                var snippet = html.Length > 200 ? html[..200] : html;
                Console.WriteLine($"    ! {pageUrl} -> 200 OK (content-type: {contentType}), but found 0 rows via the documented h5/table structure — page layout may have changed | body starts: {snippet}");
            }
            return items;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ! {pageUrl} -> request failed: {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }
}
