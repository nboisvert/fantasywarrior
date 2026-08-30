using System.Security.Cryptography;
using System.Text;
using FantasyWarrior.Core.Players;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Jobs.News;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Pulls NHL news into <see cref="NewsItem"/> from every configured source,
/// and keeps <see cref="PlayerInjury"/> in step with the two sources that are
/// injury lists.
///
/// Idempotent by <c>(Source, ExternalKey)</c>, which is a unique index — a
/// re-fetch updates rather than duplicating, and that is enforced by the
/// database rather than by this job remembering to check.
///
/// An injury list is handled differently from a feed, because it answers a
/// different question: it publishes who is hurt *today*, so a player who is no
/// longer on it has been cleared. That single fact drives both writes — his
/// PlayerInjuries row is resolved, and his NewsItem is deleted, because the
/// item said "out with a knee" and that is no longer true. The medical record
/// survives in PlayerInjuries with its ReportedUtc and ResolvedUtc; the
/// headline does not deserve to.
///
/// A source that returns nothing is treated as broken, never as "nobody is
/// hurt" — a site rewrite would otherwise clear the whole league's injuries in
/// one silent run.
///
/// Personal/non-commercial use only per the source sites' terms; Rotowire's
/// subscription-locked ANALYSIS block is never read. See
/// .claude/doc/integrations.md.
/// </summary>
public sealed class NewsSyncJob(FantasyWarriorDbContext db)
{
    private const int RetentionDays = 30;

    public async Task<int> RunAsync(IReadOnlyList<NewsSource> sources, CancellationToken ct = default)
    {
        Console.WriteLine($"=== news-sync  {sources.Count} source(s) ===");

        // Name matching is done in memory against the whole player list: it is
        // ~1,500 rows and every headline needs it, so one read beats a query
        // per item. PlayerNameIndex owns the rules — including the initial
        // fallback that a veteran between contracts needs, since we store him
        // as "R. Gudas" while every news site writes "Radko Gudas".
        var players = await db.Players
            .Select(p => new { p.PlayerId, p.FirstName, p.LastName })
            .ToListAsync(ct);
        var names = new PlayerNameIndex(players.Select(p => (p.PlayerId, p.FirstName, p.LastName)));

        var now = DateTime.UtcNow;
        int inserted = 0, updated = 0;
        // Named at the end rather than counted. An unmatched name is the one
        // failure this job has that nothing else reveals: the item is stored
        // with a null PlayerId, no screen shows it, and an injured player
        // simply never gets marked. Printing who was missed is what makes that
        // reviewable from a job log.
        var unmatched = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            var items = await source.Fetch(ct);
            Console.WriteLine($"  {source.Name}: {items.Count} item(s)");

            var seenKeys = new List<string>();
            var listed = new List<ListedInjury>();

            foreach (var item in items)
            {
                var key = ExternalKey(item);
                seenKeys.Add(key);
                var existing = await db.NewsItems
                    .FirstOrDefaultAsync(n => n.Source == source.Name && n.ExternalKey == key, ct);

                var playerName = item.PlayerNameHint ?? PlayerNameFromHeadline(item.Headline);
                var playerId = names.Resolve(playerName);
                if (playerName is not null && playerId is null)
                    unmatched.Add(playerName);

                if (source.IsInjuryList && playerId is not null)
                    listed.Add(new ListedInjury(
                        playerId.Value, InjuryClassifier.StatusFor(item.InjuryType), Truncate(item.InjuryType, 80)));

                if (existing is null)
                {
                    db.NewsItems.Add(new NewsItem
                    {
                        Source = source.Name,
                        ExternalKey = key,
                        Headline = Truncate(item.Headline, 500)!,
                        Url = Truncate(item.Url, 500),
                        Body = Truncate(item.Body, 1000),
                        PlayerId = playerId,
                        PlayerName = Truncate(playerName, 120),
                        PublishedUtc = item.PublishedUtc.UtcDateTime,
                        FetchedUtc = now,
                    });
                    inserted++;
                }
                else
                {
                    existing.Headline = Truncate(item.Headline, 500)!;
                    existing.Url = Truncate(item.Url, 500);
                    existing.Body = Truncate(item.Body, 1000);
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

            if (source.IsInjuryList && items.Count > 0)
                await ReconcileInjuriesAsync(source.Name, seenKeys, listed, now, ct);
            else if (source.IsInjuryList)
                Console.WriteLine($"    ! {source.Name} returned nothing — leaving its injuries as they stand rather than clearing them");
        }

        // Age retires a *headline*, which stops being interesting once it is a
        // month old. It says nothing about a *condition*: Troy Terry's hip was
        // reported on 18 June and he is still out, so pruning by date deleted
        // him every night and re-inserted him on the next run — churn, and his
        // card lost the one item explaining the mark on his row. An injury
        // list has its own deletion rule, and it is the true one: the source
        // stopped listing him. See ReconcileInjuriesAsync.
        var injurySources = sources.Where(s => s.IsInjuryList).Select(s => s.Name).ToList();
        var cutoff = now.AddDays(-RetentionDays);
        var pruned = await db.NewsItems
            .Where(n => n.PublishedUtc < cutoff && !injurySources.Contains(n.Source))
            .ExecuteDeleteAsync(ct);

        Console.WriteLine($"\n{inserted} new, {updated} updated, {pruned} pruned past {RetentionDays} days.");
        if (unmatched.Count > 0)
            Console.WriteLine(
                $"{unmatched.Count} name(s) matched no player, so nothing about them can be shown: " +
                string.Join(", ", unmatched));
        return 0;
    }

    /// <summary>
    /// Brings one source's open injuries — and its news items — in line with
    /// what it lists today. Called only when the fetch actually returned
    /// something; see the class remarks.
    /// </summary>
    private async Task ReconcileInjuriesAsync(
        string source, List<string> seenKeys, List<ListedInjury> listed, DateTime now, CancellationToken ct)
    {
        var dropped = await db.NewsItems
            .Where(n => n.Source == source && !seenKeys.Contains(n.ExternalKey))
            .ExecuteDeleteAsync(ct);

        var open = await db.PlayerInjuries
            .Where(i => i.Source == source && i.ResolvedUtc == null)
            .Select(i => new OpenInjury(i.PlayerInjuryId, i.PlayerId, i.Status, i.InjuryType))
            .ToListAsync(ct);

        var plan = InjuryReconciler.Plan(open, listed);

        foreach (var l in plan.ToOpen)
            db.PlayerInjuries.Add(new PlayerInjury
            {
                PlayerId = l.PlayerId,
                Status = l.Status,
                InjuryType = l.InjuryType,
                Source = source,
                ReportedUtc = now,
            });

        if (plan.ToRelabel.Count > 0 || plan.ToResolve.Count > 0)
        {
            var touchedIds = plan.ToRelabel.Select(r => r.PlayerInjuryId).Concat(plan.ToResolve).ToList();
            var rows = await db.PlayerInjuries
                .Where(i => touchedIds.Contains(i.PlayerInjuryId))
                .ToDictionaryAsync(i => i.PlayerInjuryId, ct);

            foreach (var (id, injuryType) in plan.ToRelabel)
                if (rows.TryGetValue(id, out var row)) row.InjuryType = injuryType;
            foreach (var id in plan.ToResolve)
                if (rows.TryGetValue(id, out var row)) row.ResolvedUtc = now;
        }

        await db.SaveChangesAsync(ct);
        Console.WriteLine(
            $"    injuries: {plan.ToOpen.Count} new, {plan.ToRelabel.Count} relabelled, " +
            $"{plan.ToResolve.Count} cleared, {dropped} stale item(s) dropped");
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
