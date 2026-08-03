using System.Collections.Concurrent;
using FantasyWarrior.Data;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>
/// Everything the API knows about presence in memory: who currently holds a
/// live connection, and when each username's <c>LastSeenUtc</c> was last
/// written.
///
/// <b>In-memory is correct here only because the Container App runs a single
/// replica</b> (<c>--max-replicas 1</c> in api-deploy.yml). Two replicas would
/// each hold half the connections and each believe the other half is offline —
/// the same reason SignalR itself needs a backplane past one replica. If that
/// limit is ever lifted, this class is the second thing to move to a shared
/// store, not an afterthought.
/// </summary>
public sealed class PresenceRegistry
{
    /// <summary>
    /// Connections per user, not a flag: one GM with the app open on his phone
    /// and his laptop must not go dark because he closed one of them.
    /// </summary>
    private readonly ConcurrentDictionary<int, int> _connections = new();

    private readonly ConcurrentDictionary<string, DateTime> _lastStamped = new(StringComparer.Ordinal);

    /// <summary>
    /// How long a username's <c>LastSeenUtc</c> is considered fresh enough to
    /// skip the write. Presence is read at a five-minute resolution at worst,
    /// so a minute of staleness is invisible — and without this every single
    /// request would be a write against the free serverless tier.
    /// </summary>
    private static readonly TimeSpan StampInterval = TimeSpan.FromSeconds(60);

    public bool IsOnline(int userId) => _connections.TryGetValue(userId, out var n) && n > 0;

    /// <summary>Registers a connection; true when this is the user's first.</summary>
    public bool Connect(int userId) =>
        _connections.AddOrUpdate(userId, 1, (_, n) => n + 1) == 1;

    /// <summary>Drops a connection; true when it was the user's last.</summary>
    public bool Disconnect(int userId)
    {
        var remaining = _connections.AddOrUpdate(userId, 0, (_, n) => Math.Max(0, n - 1));
        if (remaining > 0) return false;

        _connections.TryRemove(userId, out _);
        return true;
    }

    /// <summary>
    /// Writes <c>LastSeenUtc</c>, at most once a minute per user. Failures are
    /// swallowed by the caller: a presence stamp is never worth failing the
    /// request it rode in on.
    /// </summary>
    public async Task StampAsync(
        FantasyWarriorDbContext db, string normalizedUsername, DateTime nowUtc, CancellationToken ct = default)
    {
        if (_lastStamped.TryGetValue(normalizedUsername, out var last) && nowUtc - last < StampInterval)
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
