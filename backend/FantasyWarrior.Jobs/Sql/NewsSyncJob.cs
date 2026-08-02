using System.Security.Cryptography;
using System.Text;
using FantasyWarrior.Core.Players;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Jobs.News;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Pulls NHL news into <see cref="NewsItem"/> from every configured source.
///
/// Idempotent by <c>(Source, ExternalKey)</c>, which is a unique index — a
/// re-fetch updates rather than duplicating, and that is enforced by the
/// database rather than by this job remembering to check.
///
/// Personal/non-commercial use only per the source sites' terms; Rotowire's
/// subscription-locked ANALYSIS block is never read. See
/// .claude/doc/news-integration-guide.md.
/// </summary>
public sealed class NewsSyncJob(FantasyWarriorDbContext db)
{
    private const int RetentionDays = 30;

    public async Task<int> RunAsync(IReadOnlyList<NewsSource> sources, CancellationToken ct = default)
    {
        Console.WriteLine($"=== news-sync  {sources.Count} source(s) ===");

        // Name matching is done in memory against the whole player list: it is
        // ~1,500 rows and every headline needs it, so one read beats a query
        // per item.
        var players = await db.Players
            .Select(p => new { p.PlayerId, p.FirstName, p.LastName })
            .ToListAsync(ct);
        var byName = players
            .GroupBy(p => NameNormalizer.Normalize($"{p.FirstName} {p.LastName}"))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().PlayerId);

        var now = DateTime.UtcNow;
        int inserted = 0, updated = 0;

        foreach (var source in sources)
        {
            var items = await source.Fetch(ct);
            Console.WriteLine($"  {source.Name}: {items.Count} item(s)");

            foreach (var item in items)
            {
                var key = ExternalKey(item);
                var existing = await db.NewsItems
                    .FirstOrDefaultAsync(n => n.Source == source.Name && n.ExternalKey == key, ct);

                var playerName = item.PlayerNameHint ?? PlayerNameFromHeadline(item.Headline);
                var playerId = playerName is null
                    ? null
                    : byName.TryGetValue(NameNormalizer.Normalize(playerName), out var id) ? id : (long?)null;

                if (existing is null)
                {
                    db.NewsItems.Add(new NewsItem
                    {
                        Source = source.Name,
                        ExternalKey = key,
                        Headline = Truncate(item.Headline, 500),
                        Url = Truncate(item.Url, 500),
                        PlayerId = playerId,
                        PlayerName = Truncate(playerName, 120),
                        PublishedUtc = item.PublishedUtc.UtcDateTime,
                        FetchedUtc = now,
                    });
                    inserted++;
                }
                else
                {
                    existing.Headline = Truncate(item.Headline, 500);
                    existing.Url = Truncate(item.Url, 500);
                    existing.PlayerId = playerId;
                    existing.PlayerName = Truncate(playerName, 120);
                    // A scraped source carries no per-item date, so re-stamping
                    // it would make an unchanged standing injury look freshly
                    // published every night.
                    if (source.HasReliablePublishedDate)
                        existing.PublishedUtc = item.PublishedUtc.UtcDateTime;
                    existing.FetchedUtc = now;
                    updated++;
                }
            }
            await db.SaveChangesAsync(ct);
        }

        var cutoff = now.AddDays(-RetentionDays);
        var pruned = await db.NewsItems.Where(n => n.PublishedUtc < cutoff).ExecuteDeleteAsync(ct);

        Console.WriteLine($"\n{inserted} new, {updated} updated, {pruned} pruned past {RetentionDays} days.");
        return 0;
    }

    /// <summary>
    /// A stable per-source identity. The feed's own guid when it has one,
    /// otherwise a hash of the headline — scraped pages have no id, and using
    /// the headline text directly would break the moment a site pads it.
    /// </summary>
    private static string ExternalKey(NewsFeedItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ExternalId)) return Truncate(item.ExternalId, 200)!;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(item.Headline));
        return Convert.ToHexString(bytes)[..32];
    }

    /// <summary>
    /// Both sites write "Player Name: what happened", so the text before the
    /// first colon is the player. Best effort — an unmatched name is kept as
    /// text rather than dropping the item.
    /// </summary>
    private static string? PlayerNameFromHeadline(string headline)
    {
        var colon = headline.IndexOf(':');
        return colon > 0 ? headline[..colon].Trim() : null;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
