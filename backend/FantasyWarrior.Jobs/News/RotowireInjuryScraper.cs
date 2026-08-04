using System.Globalization;
using System.Net;
using HtmlAgilityPack;

namespace FantasyWarrior.Jobs.News;

/// <summary>
/// Rotowire's RSS feed only carries plain-fact headlines and can be quiet
/// off-season (confirmed live — see NewsSyncJob's other source); this HTML
/// fallback scrapes the richer injuries page instead, per the integration
/// guide's "Système 1, Étape 2". Structure re-confirmed against a live fetch
/// on 2026-08-04 and captured as <c>Fixtures/rotowire-injuries.html</c>:
///
/// &lt;div class="news-update is-injured"&gt;
///   &lt;div class="news-update__top"&gt;
///     &lt;img class="news-update__logo" alt="CBJ" ...&gt;
///     &lt;div class="news-update__playerhead"&gt;
///       &lt;a class="news-update__player-link" href="/hockey/player/isac-lundestrom-5703"&gt;Isac Lundestrom&lt;/a&gt;
///       &lt;div class="news-update__headline"&gt;Set to miss start of season&lt;/div&gt;
///     &lt;/div&gt;
///   &lt;/div&gt;
///   &lt;div class="news-update__meta"&gt;…&lt;div class="news-update__inj"&gt;Achilles&lt;/div&gt;&lt;/div&gt;
///   &lt;div class="news-update__main"&gt;
///     &lt;div class="news-update__timestamp"&gt;July 19, 2026&lt;/div&gt;
///     &lt;div class="news-update__news"&gt;Lundestrom will be out until…&lt;/div&gt;
///     &lt;div class="news-update__analysis"&gt;… subscription-locked …&lt;/div&gt;
///
/// Three things this reads that the first version threw away: the injury type,
/// the factual paragraph, and the timestamp — which does exist, contrary to the
/// earlier note, so this source now carries a real published date instead of
/// being stamped with the day it was first seen.
///
/// <c>news-update__analysis</c> is deliberately never touched: it is Rotowire's
/// subscription-locked take, and on this page it holds nothing but a "subscribe
/// now" teaser anyway. Personal/non-commercial use only per Rotowire's terms.
/// Site structure can still drift — a miss logs loudly rather than failing
/// silently, same convention as FantasySpScraper.
/// </summary>
public sealed class RotowireInjuryScraper(HttpClient http)
{
    private const string ItemClass = "news-update";
    /// <summary>Set by Rotowire on the blocks that are an injury rather than
    /// some other note that shares the page (a trade, a signing). It is the
    /// site's own answer to "is this man hurt", so we use it rather than
    /// guessing from the headline.</summary>
    private const string InjuredClass = "is-injured";
    private const string PlayerLinkClass = "news-update__player-link";
    private const string HeadlineClass = "news-update__headline";
    private const string InjuryClass = "news-update__inj";
    private const string TimestampClass = "news-update__timestamp";
    private const string NewsClass = "news-update__news";

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
            var items = ParseDocument(doc, pageUrl, DateTimeOffset.UtcNow);

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

    /// <summary>
    /// The parse, split from the fetch so it can be run against a captured
    /// page in tests. <paramref name="fallbackDate"/> is used only for a block
    /// whose timestamp is missing or unreadable.
    /// </summary>
    public static IReadOnlyList<NewsFeedItem> Parse(string html, string pageUrl, DateTimeOffset fallbackDate)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return ParseDocument(doc, pageUrl, fallbackDate);
    }

    private static IReadOnlyList<NewsFeedItem> ParseDocument(HtmlDocument doc, string pageUrl, DateTimeOffset fallbackDate)
    {
        var blocks = doc.DocumentNode.SelectNodes(ClassXPath(ItemClass, relative: false));
        var items = new List<NewsFeedItem>();
        foreach (var block in blocks ?? Enumerable.Empty<HtmlNode>())
        {
            if (!HasClass(block, InjuredClass))
                continue;

            var playerLink = block.SelectSingleNode(ClassXPath(PlayerLinkClass, relative: true))
                ?? block.SelectSingleNode(".//a[contains(@href, '/hockey/player/')]");
            if (playerLink is null)
                continue;

            var player = Text(playerLink);
            if (string.IsNullOrEmpty(player))
                continue;

            var href = playerLink.GetAttributeValue("href", null);
            var url = href is null ? pageUrl : new Uri(new Uri(pageUrl), href).ToString();
            var headlineText = TextOf(block, HeadlineClass);
            var injury = TextOf(block, InjuryClass);
            var body = TextOf(block, NewsClass);

            items.Add(new NewsFeedItem(
                // The player's own page, not the headline: this list states a
                // current condition, so a reworded update about the same knee
                // is the same item rather than a second one.
                ExternalId: href ?? player,
                Headline: string.IsNullOrEmpty(headlineText)
                    ? string.IsNullOrEmpty(injury) ? player : $"{player}: {injury}"
                    : $"{player}: {headlineText}",
                Url: url,
                PlayerNameHint: player,
                PublishedUtc: ParseTimestamp(TextOf(block, TimestampClass)) ?? fallbackDate,
                Body: string.IsNullOrEmpty(body) ? null : body,
                InjuryType: string.IsNullOrEmpty(injury) ? null : injury));
        }

        return items;
    }

    /// <summary>"August 1, 2026" — the site's own format, read as a plain date
    /// at UTC midnight. No time of day is published, and inventing one would
    /// only make two items from the same day sort by accident.</summary>
    public static DateTimeOffset? ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.TryParseExact(
            text.Trim(), ["MMMM d, yyyy", "MMM d, yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
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
        Console.WriteLine($"    ! {pageUrl} -> 200 OK (content-type: {contentType}), found real player links but no '{ItemClass} {InjuredClass}' block matched — 3-levels-up ancestor of the first player link: {snippet}");
    }

    private static bool HasClass(HtmlNode node, string className) =>
        $" {node.GetAttributeValue("class", "").Trim()} ".Contains($" {className} ", StringComparison.Ordinal);

    private static string Text(HtmlNode node) => WebUtility.HtmlDecode(node.InnerText).Trim();

    private static string TextOf(HtmlNode block, string className)
    {
        var node = block.SelectSingleNode(ClassXPath(className, relative: true));
        return node is null ? "" : Text(node);
    }

    private static string ClassXPath(string className, bool relative)
        => $"{(relative ? ".//" : "//")}*[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]";
}
