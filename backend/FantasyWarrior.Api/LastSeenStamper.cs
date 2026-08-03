using System.Collections.Concurrent;
using FantasyWarrior.Data;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>
/// Writes <c>User.LastSeenUtc</c> from ordinary traffic, at most once a minute
/// per user.
///
/// Split from <see cref="PresenceService"/> (2026-08-03): one holds live
/// connections in memory and answers "is this person here right now", the other
/// writes a timestamp to SQL so an *absent* person's row can say "45min ago".
/// They were one class, which is what made presence feel like it lived in two
/// places at once.
/// </summary>
public sealed class LastSeenStamper
{
    private readonly ConcurrentDictionary<string, DateTime> _lastStamped = new(StringComparer.Ordinal);

    /// <summary>
    /// How long a stamp stays fresh enough to skip the write. Presence labels
    /// are read at a minute's resolution at best, so this is invisible — and
    /// without it every single request would be a write against the free
    /// serverless tier.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public async Task StampAsync(
        FantasyWarriorDbContext db, string normalizedUsername, DateTime nowUtc, CancellationToken ct = default)
    {
        if (_lastStamped.TryGetValue(normalizedUsername, out var last) && nowUtc - last < Interval)
            return;

        // Claim the slot before the await, so a burst of concurrent requests
        // from the same user produces one write rather than a write each.
        _lastStamped[normalizedUsername] = nowUtc;

        // ExecuteUpdate rather than load-modify-save: one UPDATE, no SELECT and
        // no change tracking, on a path that runs on nearly every request.
        await db.Users
            .Where(u => u.Username == normalizedUsername)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenUtc, nowUtc), ct);
    }
}
