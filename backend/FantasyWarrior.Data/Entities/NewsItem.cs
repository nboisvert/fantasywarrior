namespace FantasyWarrior.Data.Entities;

public static class NewsSourceName
{
    public const string RotowireRss = "rotowire_rss";
    public const string RotowireHtml = "rotowire_html";
    public const string FantasySp = "fantasysp";
}

/// <summary>
/// One NHL news headline. Global, not league-scoped — the ticker's news half is
/// a generic NHL feed shown to everyone, unfiltered by roster.
///
/// Personal/non-commercial use only per both source sites' terms; never store
/// Rotowire's subscription-locked "ANALYSIS" body. See
/// .claude/doc/news-integration-guide.md.
/// </summary>
public sealed class NewsItem
{
    public int NewsItemId { get; set; }

    /// <summary>See <see cref="NewsSourceName"/>.</summary>
    public required string Source { get; set; }

    /// <summary>
    /// Stable per-source identity (feed guid, or a hash of the headline for
    /// scraped sources). Unique, so a re-fetch updates rather than duplicates.
    /// </summary>
    public required string ExternalKey { get; set; }

    public required string Headline { get; set; }

    public string? Url { get; set; }

    /// <summary>Best-effort name match against our player table; null when unmatched.</summary>
    public long? PlayerId { get; set; }

    /// <summary>The name as the source wrote it, kept even when the match fails.</summary>
    public string? PlayerName { get; set; }

    /// <summary>
    /// Scraped sources carry no per-item date, so this is preserved across
    /// re-fetches rather than re-stamped — otherwise an unchanged standing
    /// injury would look freshly published every night.
    /// </summary>
    public DateTime PublishedUtc { get; set; }

    public DateTime FetchedUtc { get; set; }

    public Player? Player { get; set; }
}
