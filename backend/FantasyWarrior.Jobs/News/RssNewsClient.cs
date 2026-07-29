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
    /// <summary>Never throws; a miss returns [] but always logs why (status
    /// code, content-type, parse failure, or 0 &lt;item&gt; elements found) so a
    /// bad feed URL is diagnosable from job output instead of silently
    /// looking like "no news today".</summary>
    public async Task<IReadOnlyList<RssFeedItem>> GetItemsAsync(string feedUrl, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(feedUrl, ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"    ! {feedUrl} -> {(int)response.StatusCode} {response.StatusCode} (content-type: {contentType})");
                return [];
            }

            var xml = await response.Content.ReadAsStringAsync(ct);
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (Exception ex)
            {
                var snippet = xml.Length > 120 ? xml[..120] : xml;
                Console.WriteLine($"    ! {feedUrl} -> 200 OK (content-type: {contentType}) but not parseable XML: {ex.Message} | body starts: {snippet}");
                return [];
            }

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
            if (items.Count == 0)
                Console.WriteLine($"    ! {feedUrl} -> 200 OK (content-type: {contentType}), parsed as XML, but found 0 <item> elements — feed shape may have changed");
            return items;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ! {feedUrl} -> request failed: {ex.GetType().Name}: {ex.Message}");
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
