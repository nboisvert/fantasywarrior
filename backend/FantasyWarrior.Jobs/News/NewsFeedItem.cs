namespace FantasyWarrior.Jobs.News;

/// <summary>
/// Source-agnostic shape a news fetcher (RSS client, HTML scraper, ...)
/// produces — NewsSyncJob doesn't care how an item was obtained, only that
/// it has these fields. <see cref="PlayerNameHint"/> is set when the source
/// itself identifies the player explicitly (e.g. a dedicated table column),
/// which is more reliable than NewsSyncJob's own headline-colon heuristic —
/// null when the source doesn't carry that information (plain RSS).
///
/// <paramref name="Body"/> is the factual paragraph under the headline, which
/// is what makes a player's news tab worth opening. Rotowire's
/// subscription-locked ANALYSIS block is never it — see the scrapers.
/// <paramref name="InjuryType"/> is the source's own short label ("Achilles",
/// "Suspension"); only an injury list sets it, and it is what
/// <see cref="InjuryClassifier"/> reads.
/// </summary>
public sealed record NewsFeedItem(
    string ExternalId,
    string Headline,
    string Url,
    string? PlayerNameHint,
    DateTimeOffset PublishedUtc,
    string? Body = null,
    string? InjuryType = null);

/// <summary>A source's fetch operation — returns [] rather than throwing on
/// any failure, logging why so a broken source is diagnosable from job
/// output (see RssNewsClient/FantasySpScraper).</summary>
public delegate Task<IReadOnlyList<NewsFeedItem>> NewsFetcher(CancellationToken ct);

/// <summary>
/// One configured news source.
///
/// <paramref name="HasReliablePublishedDate"/> matters because a source with
/// no per-item date would otherwise look freshly published on every sync;
/// those sources keep whatever date they were first seen with.
///
/// <paramref name="IsInjuryList"/> says the fetch returns *who is hurt right
/// now*, not a stream of events. That is a different kind of answer: a player
/// dropping off the page is itself news (he is cleared), which is what lets
/// NewsSyncJob keep PlayerInjuries in step without any source telling it so.
/// </summary>
public sealed record NewsSource(
    string Name,
    NewsFetcher Fetch,
    bool HasReliablePublishedDate,
    bool IsInjuryList = false);
