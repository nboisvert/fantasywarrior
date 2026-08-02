namespace FantasyWarrior.Jobs.News;

/// <summary>
/// Source-agnostic shape a news fetcher (RSS client, HTML scraper, ...)
/// produces — NewsSyncJob doesn't care how an item was obtained, only that
/// it has these fields. <see cref="PlayerNameHint"/> is set when the source
/// itself identifies the player explicitly (e.g. a dedicated table column),
/// which is more reliable than NewsSyncJob's own headline-colon heuristic —
/// null when the source doesn't carry that information (plain RSS).
/// </summary>
public sealed record NewsFeedItem(
    string ExternalId,
    string Headline,
    string Url,
    string? PlayerNameHint,
    DateTimeOffset PublishedUtc);

/// <summary>A source's fetch operation — returns [] rather than throwing on
/// any failure, logging why so a broken source is diagnosable from job
/// output (see RssNewsClient/FantasySpScraper).</summary>
public delegate Task<IReadOnlyList<NewsFeedItem>> NewsFetcher(CancellationToken ct);

/// <summary>
/// One configured news source.
///
/// <paramref name="HasReliablePublishedDate"/> is the one that matters: a
/// scraped injuries table carries no per-item date, so re-stamping it on every
/// sync would make an unchanged standing injury look freshly published forever.
/// Those sources keep whatever date they were first seen with.
/// </summary>
public sealed record NewsSource(string Name, NewsFetcher Fetch, bool HasReliablePublishedDate);
