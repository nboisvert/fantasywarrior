using System.Collections.Concurrent;

namespace FantasyWarrior.Core.Messaging;

/// <summary>
/// Who is connected, per league. The single source of truth for it, and it
/// never touches the database.
///
/// In Core rather than beside the hub that drives it: there is not a line of
/// SignalR or ASP.NET in here, it is two dictionaries and a refcount, and Core
/// is the project whose tests actually run in CI (Data.Tests skip for want of a
/// SQL Server on the runner).
///
/// That last part is the point: presence changes on every connect and
/// disconnect, and the process already knows the answer, so asking SQL for it
/// would be paying a round-trip to be told what we just did. The database
/// still holds <c>LastSeenUtc</c>, but only to word the label for people who
/// are *not* online — see <c>Presence</c> in FantasyWarrior.Core.
///
/// <b>In-memory is correct here only because the Container App runs a single
/// replica</b> (<c>--max-replicas 1</c> in api-deploy.yml). Two replicas would
/// each hold half the connections and each report the other half as offline.
/// This is now the only memory of who is online, so that ceiling is structural,
/// not a preference.
/// </summary>
public sealed class PresenceService
{
    /// <summary>
    /// The league's public code is carried along so a disconnect can label its
    /// broadcast without going back to the database — otherwise the one place
    /// that has to be fast and side-effect-free would still owe SQL a lookup.
    /// </summary>
    private sealed record Connected(int UserId, string Username, int LeagueId, string LeagueCode);

    /// <summary>
    /// By connection, so a disconnect can clean up from the connection id alone.
    /// The hub used to stash this on <c>Context.Items</c>; holding it here is
    /// what makes this class authoritative rather than a cache of the hub's
    /// bookkeeping.
    /// </summary>
    private readonly ConcurrentDictionary<string, Connected> _connections = new(StringComparer.Ordinal);

    /// <summary>
    /// league -> user -> how many connections that user holds in it.
    ///
    /// A count, not a flag: a GM with the app open on his phone and his laptop
    /// must not go dark because he closed one of them.
    /// </summary>
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, int>> _byLeague = new();

    /// <summary>
    /// Registers a connection. True when it changed who the league sees as
    /// online — i.e. this was the user's first connection there, and only then
    /// is a broadcast telling anyone something new.
    /// </summary>
    public bool Join(string connectionId, int userId, string username, int leagueId, string leagueCode)
    {
        _connections[connectionId] = new Connected(userId, username, leagueId, leagueCode);

        var league = _byLeague.GetOrAdd(leagueId, _ => new ConcurrentDictionary<int, int>());
        return league.AddOrUpdate(userId, 1, (_, n) => n + 1) == 1;
    }

    /// <summary>
    /// Removes a connection. Returns the league it belonged to (null when the
    /// connection was never registered — an aborted handshake, or a disconnect
    /// arriving twice) and whether the league's online set actually changed.
    /// </summary>
    public (int? LeagueId, string? LeagueCode, bool Changed) Leave(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var c)) return (null, null, false);
        if (!_byLeague.TryGetValue(c.LeagueId, out var league)) return (c.LeagueId, c.LeagueCode, false);

        var remaining = league.AddOrUpdate(c.UserId, 0, (_, n) => Math.Max(0, n - 1));
        if (remaining > 0) return (c.LeagueId, c.LeagueCode, false);

        league.TryRemove(c.UserId, out _);
        return (c.LeagueId, c.LeagueCode, true);
    }

    /// <summary>
    /// The league's online usernames — the entire payload of a presence
    /// broadcast. Ordinal-sorted so two calls with the same state produce the
    /// same array and the client never re-renders over nothing.
    /// </summary>
    public IReadOnlyList<string> OnlineUsernames(int leagueId)
    {
        if (!_byLeague.TryGetValue(leagueId, out var league)) return [];

        var online = league.Keys.ToHashSet();
        return _connections.Values
            .Where(c => c.LeagueId == leagueId && online.Contains(c.UserId))
            .Select(c => c.Username)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Whether a user is connected <i>to this league</i>. Presence is
    /// league-scoped on purpose: a connection carries one league, so someone
    /// looking at another pool is not "here".
    /// </summary>
    public bool IsOnline(int leagueId, int userId) =>
        _byLeague.TryGetValue(leagueId, out var league)
        && league.TryGetValue(userId, out var n)
        && n > 0;
}
