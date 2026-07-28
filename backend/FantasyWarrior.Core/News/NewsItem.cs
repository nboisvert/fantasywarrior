using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.News;

/// <summary>
/// One article/note from an external NHL news feed (Rotowire, FantasySP, ...).
/// Global collection `news/{id}` — not scoped to a league, since the same
/// injury/lineup/rumor item is relevant to every league. Doc id is a
/// deterministic hash of source + the feed's own guid/link, so re-running
/// NewsSyncJob upserts idempotently instead of duplicating.
/// </summary>
[FirestoreData]
public sealed class NewsItem
{
    /// <summary>"rotowire" or "fantasysp".</summary>
    [FirestoreProperty("source")]
    public string Source { get; set; } = "";

    [FirestoreProperty("headline")]
    public string Headline { get; set; } = "";

    [FirestoreProperty("url")]
    public string Url { get; set; } = "";

    /// <summary>Best-effort match against the `players` collection via
    /// NameNormalizer. Null when the feed item couldn't be matched to a
    /// known player (item is still stored, with the raw PlayerName).</summary>
    [FirestoreProperty("playerId")]
    public long? PlayerId { get; set; }

    [FirestoreProperty("playerName")]
    public string? PlayerName { get; set; }

    [FirestoreProperty("publishedUtc")]
    public Timestamp PublishedUtc { get; set; }

    [FirestoreProperty("fetchedUtc")]
    public Timestamp FetchedUtc { get; set; }
}
