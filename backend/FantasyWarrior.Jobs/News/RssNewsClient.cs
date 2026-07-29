using System.Globalization;
using System.Xml.Linq;

namespace FantasyWarrior.Jobs.News;

/// <summary>
/// Thin client for standard RSS 2.0 feeds (Rotowire publishes NHL news this
/// way — see the vendor guide's Système 1) — not tied to a specific site's
/// shape, just the common &lt;item&gt;&lt;title&gt;&lt;link&gt;&lt;guid&gt;&lt;pubDate&gt;
/// structure. Same "return [] on failure rather than throw" convention as
/// NhlApiClient, but always logs *why* a miss happened (status code,
/// content-type, parse failure, or 0 &lt;item&gt; elements found) so a bad
/// feed URL — or a feed that's just quiet off-season — is diagnosable from
/// job output instead of silently looking like "no news today".
/// </summary>
public sealed class RssNewsClient(HttpClient http)
{
    public async Task<IReadOnlyList<NewsFeedItem>> GetItemsAsync(string feedUrl, CancellationToken ct = default)
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
                var snippet = xml.Length > 200 ? xml[..200] : xml;
                Console.WriteLine($"    ! {feedUrl} -> 200 OK (content-type: {contentType}) but not parseable XML: {ex.Message} | body starts: {snippet}");
                return [];
            }

            var items = new List<NewsFeedItem>();
            foreach (var item in doc.Descendants("item"))
            {
                var title = item.Element("title")?.Value.Trim();
                var link = item.Element("link")?.Value.Trim();
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link))
                    continue;
                var guid = item.Element("guid")?.Value.Trim();
                var published = ParsePubDate(item.Element("pubDate")?.Value);
                items.Add(new NewsFeedItem(
                    ExternalId: string.IsNullOrEmpty(guid) ? link : guid,
                    Headline: title,
                    Url: link,
                    PlayerNameHint: null,
                    PublishedUtc: published));
            }
            if (items.Count == 0)
            {
                var snippet = xml.Length > 200 ? xml[..200] : xml;
                Console.WriteLine($"    ! {feedUrl} -> 200 OK (content-type: {contentType}), parsed as XML, but found 0 <item> elements (feed shape changed, or genuinely quiet right now) | body starts: {snippet}");
            }
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
