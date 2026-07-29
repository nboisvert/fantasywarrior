using System.Globalization;
using System.Net;
using HtmlAgilityPack;

namespace FantasyWarrior.Jobs.News;

/// <summary>
/// Rotowire's RSS feed only carries plain-fact headlines and can be quiet
/// off-season (confirmed live — see NewsSyncJob's other source); this HTML
/// fallback scrapes the richer injuries page instead, per the integration
/// guide's "Système 1, Étape 2", for team/injury-type/date detail the RSS
/// doesn't carry. Selectors are the guide's own documented placeholders
/// (repeating "news-item" blocks, each with a player link plus team-name/
/// injury-type/date/headline/news-body children) — unverified against live
/// DOM (the guide itself flags this), so a miss logs loudly rather than
/// failing silently, same convention as FantasySpScraper. Deliberately
/// never captures an "ANALYSIS" block (subscription-locked content) — only
/// the factual news-body, matching the guide's own placeholder which does
/// the same. Personal/non-commercial use only per Rotowire's terms.
/// </summary>
public sealed class RotowireInjuryScraper(HttpClient http)
{
    private const string ItemClass = "news-item";

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
                var playerLink = block.SelectSingleNode(".//a[contains(@href, '/hockey/player/')]");
                if (playerLink is null)
                    continue;

                var player = WebUtility.HtmlDecode(playerLink.InnerText).Trim();
                if (string.IsNullOrEmpty(player))
                    continue;

                var href = playerLink.GetAttributeValue("href", null);
                var url = href is null ? pageUrl : new Uri(new Uri(pageUrl), href).ToString();
                var injuryType = TextOf(block, "injury-type");
                var dateText = TextOf(block, "date");
                var headlineText = TextOf(block, "headline");
                var body = TextOf(block, "news-body");
                var published = ParseDate(dateText);

                var headline = !string.IsNullOrEmpty(headlineText)
                    ? headlineText
                    : string.IsNullOrEmpty(injuryType) ? $"{player}: {body}" : $"{player} ({injuryType}): {body}";

                items.Add(new NewsFeedItem(
                    ExternalId: $"{href}-{dateText}-{headline}",
                    Headline: headline,
                    Url: url,
                    PlayerNameHint: player,
                    PublishedUtc: published));
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

    /// <summary>The generic "first N chars of the page" a plain body-snippet
    /// diagnostic gives is always just &lt;head&gt; boilerplate, useless for
    /// figuring out the real container structure. Instead: find a real
    /// player link directly (independent of the failed 'news-item' guess),
    /// walk up a few ancestors, and print that node's actual class names —
    /// enough to write the correct selector next time without re-guessing.</summary>
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
        Console.WriteLine($"    ! {pageUrl} -> 200 OK (content-type: {contentType}), found real player links but the '{ItemClass}' block guess didn't match — 3-levels-up ancestor of the first player link: {snippet}");
    }

    private static string TextOf(HtmlNode block, string className)
    {
        var node = block.SelectSingleNode(ClassXPath(className, relative: true));
        return node is null ? "" : WebUtility.HtmlDecode(node.InnerText).Trim();
    }

    private static string ClassXPath(string className, bool relative)
        => $"{(relative ? ".//" : "//")}*[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]";

    private static DateTimeOffset ParseDate(string? raw)
    {
        if (!string.IsNullOrEmpty(raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.ToUniversalTime();
        return DateTimeOffset.UtcNow;
    }
}
