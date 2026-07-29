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
