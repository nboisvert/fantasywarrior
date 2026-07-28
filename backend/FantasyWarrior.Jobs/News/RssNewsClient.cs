using System.Globalization;
using System.Xml.Linq;

namespace FantasyWarrior.Jobs.News;

public sealed record RssFeedItem(string Guid, string Title, string Link, DateTimeOffset PublishedUtc);

/// <summary>
/// Thin client for standard RSS 2.0 feeds (Rotowire/FantasySP publish NHL
/// news this way for syndication) — not tied to either source's specific
/// shape, just the common &lt;item&gt;&lt;title&gt;&lt;link&gt;&lt;guid&gt;&lt;pubDate&gt;
/// structure. Same "return [] on failure rather than throw" convention as
/// NhlApiClient.
/// </summary>
public sealed class RssNewsClient(HttpClient http)
{
    public async Task<IReadOnlyList<RssFeedItem>> GetItemsAsync(string feedUrl, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(feedUrl, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            var xml = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);
            var items = new List<RssFeedItem>();
            foreach (var item in doc.Descendants("item"))
            {
                var title = item.Element("title")?.Value.Trim();
                var link = item.Element("link")?.Value.Trim();
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link))
                    continue;
                var guid = item.Element("guid")?.Value.Trim();
                var published = ParsePubDate(item.Element("pubDate")?.Value);
                items.Add(new RssFeedItem(
                    Guid: string.IsNullOrEmpty(guid) ? link : guid,
                    Title: title,
                    Link: link,
                    PublishedUtc: published));
            }
            return items;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static DateTimeOffset ParsePubDate(string? raw)
    {
        if (!string.IsNullOrEmpty(raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.ToUniversalTime();
        return DateTimeOffset.UtcNow;
    }
}
