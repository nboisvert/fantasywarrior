using System.Security.Cryptography;
using System.Text;
using FantasyWarrior.Core.News;
using FantasyWarrior.Core.Players;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.News;

/// <summary>One news source's fetch operation, registered with a name and
/// whether it carries a genuine per-item publish date. Rotowire's RSS feed
/// does; neither of the HTML-scraped sources (Rotowire's injuries page,
/// FantasySP's injuries table) exposes a confirmed per-item date field, so
/// for those NewsSyncJob preserves an item's original PublishedUtc across
/// reruns instead of re-stamping "now" every time — otherwise an unchanged
/// standing injury would look freshly published in the ticker on every
/// nightly run.</summary>
public sealed record NewsSource(string Name, NewsFetcher Fetch, bool HasReliablePublishedDate);

/// <summary>
/// Fetches NHL news from external sources (Rotowire RSS, FantasySP HTML —
/// see RssNewsClient/FantasySpScraper) and upserts them into the global
/// `news` collection. Idempotent: doc id is a hash of source+external id, so
/// re-running never duplicates an item. Also prunes items older than
/// <see cref="RetentionDays"/> so the collection doesn't grow forever.
/// </summary>
public sealed class NewsSyncJob(FirestoreDb db)
{
    private const int FirestoreBatchLimit = 500;
    private const int RetentionDays = 30;

    public async Task<int> RunAsync(IReadOnlyList<NewsSource> sources, CancellationToken ct = default)
    {
        var playersByName = await LoadPlayerNameIndexAsync(ct);
        var collection = db.Collection("news");
        var existingPublishedById = await LoadExistingPublishedDatesAsync(collection, ct);
        var now = Timestamp.GetCurrentTimestamp();
        var writes = new List<(DocumentReference Doc, NewsItem Item)>();

        foreach (var source in sources)
        {
            var feedItems = await source.Fetch(ct);
            Console.WriteLine($"  {source.Name}: {feedItems.Count} items");
            foreach (var feedItem in feedItems)
            {
                var (playerName, playerId) = MatchPlayer(feedItem, playersByName);
                var docId = DocId(source.Name, feedItem.ExternalId);
                var publishedUtc = Timestamp.FromDateTime(feedItem.PublishedUtc.UtcDateTime);
                if (!source.HasReliablePublishedDate && existingPublishedById.TryGetValue(docId, out var firstSeen))
                    publishedUtc = firstSeen;

                writes.Add((collection.Document(docId), new NewsItem
                {
                    Source = source.Name,
                    Headline = feedItem.Headline,
                    Url = feedItem.Url,
                    PlayerId = playerId,
                    PlayerName = playerName,
                    PublishedUtc = publishedUtc,
                    FetchedUtc = now,
                }));
            }
        }

        foreach (var chunk in writes.Chunk(FirestoreBatchLimit))
        {
            var batch = db.StartBatch();
            foreach (var (doc, item) in chunk)
                batch.Set(doc, item);
            await batch.CommitAsync(ct);
        }

        var pruned = await PruneOldAsync(collection, ct);
        Console.WriteLine($"NewsSync: upserted {writes.Count} items, pruned {pruned} older than {RetentionDays}d");
        return writes.Count;
    }

    /// <summary>Prefers the source's own player identification (e.g.
    /// FantasySP's dedicated table column) over guessing from the headline.
    /// Falls back to the Rotowire-style "Player Name: blurb" convention —
    /// best-effort only, unmatched items are still stored with PlayerId=null.</summary>
    private static (string? PlayerName, long? PlayerId) MatchPlayer(NewsFeedItem item, IReadOnlyDictionary<string, long> playersByName)
    {
        var candidate = item.PlayerNameHint;
        if (string.IsNullOrEmpty(candidate))
        {
            var colonIndex = item.Headline.IndexOf(':');
            if (colonIndex <= 0)
                return (null, null);
            candidate = item.Headline[..colonIndex].Trim();
        }
        return playersByName.TryGetValue(NameNormalizer.Normalize(candidate), out var playerId)
            ? (candidate, playerId)
            : (candidate, null);
    }

    private async Task<IReadOnlyDictionary<string, long>> LoadPlayerNameIndexAsync(CancellationToken ct)
    {
        var snapshot = await db.Collection("players").GetSnapshotAsync(ct);
        var byName = new Dictionary<string, long>();
        foreach (var doc in snapshot.Documents)
        {
            var player = doc.ConvertTo<Player>();
            byName[NameNormalizer.Normalize($"{player.FirstName} {player.LastName}")] = player.NhlId;
        }
        return byName;
    }

    private static async Task<IReadOnlyDictionary<string, Timestamp>> LoadExistingPublishedDatesAsync(CollectionReference collection, CancellationToken ct)
    {
        var snapshot = await collection.GetSnapshotAsync(ct);
        var byId = new Dictionary<string, Timestamp>();
        foreach (var doc in snapshot.Documents)
            byId[doc.Id] = doc.GetValue<Timestamp>("publishedUtc");
        return byId;
    }

    private static async Task<int> PruneOldAsync(CollectionReference collection, CancellationToken ct)
    {
        var cutoff = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(-RetentionDays));
        var stale = await collection.WhereLessThan("publishedUtc", cutoff).GetSnapshotAsync(ct);
        var deleted = 0;
        foreach (var chunk in stale.Documents.Chunk(FirestoreBatchLimit))
        {
            var batch = collection.Database.StartBatch();
            foreach (var doc in chunk)
                batch.Delete(doc.Reference);
            await batch.CommitAsync(ct);
            deleted += chunk.Length;
        }
        return deleted;
    }

    private static string DocId(string source, string externalId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{source}|{externalId}"));
        return $"{source}_{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }
}
