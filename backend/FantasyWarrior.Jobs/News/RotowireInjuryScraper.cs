using System.Net;
using HtmlAgilityPack;

namespace FantasyWarrior.Jobs.News;

/// <summary>
/// Rotowire's RSS feed only carries plain-fact headlines and can be quiet
/// off-season (confirmed live — see NewsSyncJob's other source); this HTML
/// fallback scrapes the richer injuries page instead, per the integration
/// guide's "Système 1, Étape 2". Selectors below were confirmed against a
/// live page fetch (2026-07-29) — repeating BEM-style blocks:
///
/// &lt;div class="news-update is-injured"&gt;
///   &lt;div class="news-update__top"&gt;
///     &lt;img class="news-update__logo" alt="CBJ" ...&gt;
///     &lt;div class="news-update__playerhead"&gt;
///       &lt;a class="news-update__player-link" href="/hockey/player/isac-lundestrom-5703"&gt;Isac Lundestrom&lt;/a&gt;
///       &lt;a class="news-update__headline" href="..."&gt;Set to miss start of season&lt;/a&gt;
///     &lt;/div&gt;
///   &lt;/div&gt;
///   &lt;div class="news-update__meta"&gt;&lt;div&gt;&lt;b class="news-update__pos"&gt;C&lt;/b&gt;Columbus...
///
/// No confirmed per-item date field was found in the captured structure, so
/// (like FantasySP) this source is flagged as not having a reliable
/// published date — NewsSyncJob preserves an item's first-seen timestamp
/// across reruns instead of guessing "now" every time. Deliberately never
/// captures an "ANALYSIS" block (subscription-locked content) — this page
/// section doesn't appear to expose one anyway (that's Rotowire's separate
/// player-page tab, not the injuries list). Personal/non-commercial use
/// only per Rotowire's terms. Site structure can still drift — a miss logs
/// loudly rather than failing silently, same convention as FantasySpScraper.
/// </summary>
public sealed class RotowireInjuryScraper(HttpClient http)
{
    private const string ItemClass = "news-update";
    private const string PlayerLinkClass = "news-update__player-link";
    private const string HeadlineClass = "news-update__headline";
    private const string LogoClass = "news-update__logo";
    private const string PosClass = "news-update__pos";

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

            var blocks = doc.DocumentNode.SelectNodes(ClassXPath(ItemClass, relative: false));
            var items = new List<NewsFeedItem>();
            foreach (var block in blocks ?? Enumerable.Empty<HtmlNode>())
            {
                var playerLink = block.SelectSingleNode(ClassXPath(PlayerLinkClass, relative: true))
                    ?? block.SelectSingleNode(".//a[contains(@href, '/hockey/player/')]");
                if (playerLink is null)
                    continue;

                var player = WebUtility.HtmlDecode(playerLink.InnerText).Trim();
                if (string.IsNullOrEmpty(player))
                    continue;

                var href = playerLink.GetAttributeValue("href", null);
                var url = href is null ? pageUrl : new Uri(new Uri(pageUrl), href).ToString();
                var headlineText = TextOf(block, HeadlineClass);
                var pos = TextOf(block, PosClass);
                var teamAbbrev = block.SelectSingleNode(ClassXPath(LogoClass, relative: true))?.GetAttributeValue("alt", "") ?? "";

                var headline = !string.IsNullOrEmpty(headlineText)
                    ? $"{player}: {headlineText}"
                    : string.IsNullOrEmpty(pos) && string.IsNullOrEmpty(teamAbbrev) ? player : $"{player} ({pos} {teamAbbrev})".Trim();

                items.Add(new NewsFeedItem(
                    ExternalId: $"{href}-{headlineText}",
                    Headline: headline,
                    Url: url,
                    PlayerNameHint: player,
                    PublishedUtc: DateTimeOffset.UtcNow));
            }

            if (items.Count == 0)
                LogZeroItemsDiagnostic(doc, pageUrl, contentType);
            return items;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ! {pageUrl} -> request failed: {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }

    /// <summary>The generic "first N chars of the page" diagnostic is always
    /// just &lt;head&gt; boilerplate, useless for figuring out the real
    /// container structure. Instead: find a real player link directly
    /// (independent of the item-block class), walk up a few ancestors, and
    /// print that node's actual class names — enough to fix the selector
    /// without re-guessing blind.</summary>
    private static void LogZeroItemsDiagnostic(HtmlDocument doc, string pageUrl, string contentType)
    {
        var anyPlayerLink = doc.DocumentNode.SelectSingleNode("//a[contains(@href, '/hockey/player/')]");
        if (anyPlayerLink is null)
        {
            Console.WriteLine($"    ! {pageUrl} -> 200 OK (content-type: {contentType}), but no '/hockey/player/' links found anywhere in the parsed HTML at all — content may be client-rendered (JS), or the URL pattern assumption is wrong");
            return;
        }

        var ancestor = anyPlayerLink;
        for (var i = 0; i < 3 && ancestor.ParentNode is { Name: not "body" }; i++)
            ancestor = ancestor.ParentNode;
        var outerHtml = ancestor.OuterHtml;
        var snippet = outerHtml.Length > 600 ? outerHtml[..600] : outerHtml;
        Console.WriteLine($"    ! {pageUrl} -> 200 OK (content-type: {contentType}), found real player links but the '{ItemClass}' block selector didn't match — 3-levels-up ancestor of the first player link: {snippet}");
    }

    private static string TextOf(HtmlNode block, string className)
    {
        var node = block.SelectSingleNode(ClassXPath(className, relative: true));
        return node is null ? "" : WebUtility.HtmlDecode(node.InnerText).Trim();
    }

    private static string ClassXPath(string className, bool relative)
        => $"{(relative ? ".//" : "//")}*[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]";
}
