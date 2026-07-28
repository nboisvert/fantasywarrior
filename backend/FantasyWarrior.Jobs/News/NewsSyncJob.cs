using System.Security.Cryptography;
using System.Text;
using FantasyWarrior.Core.News;
using FantasyWarrior.Core.Players;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.News;

public sealed record NewsFeedSource(string Source, string FeedUrl);

/// <summary>
/// Fetches NHL news from external RSS feeds (Rotowire, FantasySP) and
/// upserts them into the global `news` collection. Idempotent: doc id is a
/// hash of source+guid, so re-running never duplicates an item. Also prunes
/// items older than <see cref="RetentionDays"/> so the collection doesn't
/// grow forever.
/// </summary>
public sealed class NewsSyncJob(RssNewsClient client, FirestoreDb db)
{
    private const int FirestoreBatchLimit = 500;
    private const int RetentionDays = 30;

    public async Task<int> RunAsync(IReadOnlyList<NewsFeedSource> sources, CancellationToken ct = default)
    {
        var playersByName = await LoadPlayerNameIndexAsync(ct);

        var collection = db.Collection("news");
        var now = Timestamp.GetCurrentTimestamp();
        var writes = new List<(DocumentReference Doc, NewsItem Item)>();

        foreach (var source in sources)
        {
            var feedItems = await client.GetItemsAsync(source.FeedUrl, ct);
            Console.WriteLine($"  {source.Source}: {feedItems.Count} items");
            foreach (var feedItem in feedItems)
            {
                var (playerName, playerId) = MatchPlayer(feedItem.Title, playersByName);
                var docId = DocId(source.Source, feedItem.Guid);
                writes.Add((collection.Document(docId), new NewsItem
                {
                    Source = source.Source,
                    Headline = feedItem.Title,
                    Url = feedItem.Link,
                    PlayerId = playerId,
                    PlayerName = playerName,
                    PublishedUtc = Timestamp.FromDateTime(feedItem.PublishedUtc.UtcDateTime),
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

    /// <summary>Rotowire/FantasySP headlines conventionally lead with the
    /// player's name followed by a colon (e.g. "Auston Matthews: Day-to-day
    /// with upper-body injury") — best-effort only, unmatched items are still
    /// stored with PlayerId=null.</summary>
    private static (string? PlayerName, long? PlayerId) MatchPlayer(string headline, IReadOnlyDictionary<string, long> playersByName)
    {
        var colonIndex = headline.IndexOf(':');
        if (colonIndex <= 0)
            return (null, null);
        var candidate = headline[..colonIndex].Trim();
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

    private static string DocId(string source, string guid)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{source}|{guid}"));
        return $"{source}_{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }
}
