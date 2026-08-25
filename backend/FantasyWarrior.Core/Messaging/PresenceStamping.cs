namespace FantasyWarrior.Core.Messaging;

/// <summary>
/// Which username a request proves is *present* — the viewer, never the viewed.
///
/// Split out of the API's presence middleware on 2026-08-25, after a bug that
/// made the whole league look active. The middleware read <c>username</c> from
/// the route values first, and on every league-scoped team route that segment
/// names the team's *owner*, not the caller: opening a rival's roster stamped
/// the rival as "seen just now". Eight GMs who had never logged in read as
/// "4h ago", and a genuine first login was indistinguishable from someone
/// having been looked at.
///
/// The rule that replaces it: <b>only the query string names the viewer.</b>
/// A route segment is trusted for this only where it addresses the caller
/// themselves — the <c>/api/users/{username}/…</c> family, which is how the app
/// asks "what are *my* leagues" right after login and is the only route family
/// where subject and caller are the same person by construction.
/// </summary>
public static class PresenceStamping
{
    /// <summary>The one route family whose {username} segment is the caller.</summary>
    private const string SelfAddressed = "/api/users/";

    /// <summary>
    /// The username to stamp, or null when the request proves nothing about
    /// who is there.
    ///
    /// <paramref name="queryViewer"/> wins over <paramref name="queryUsername"/>
    /// because a route that carries both is one where <c>username</c> is the
    /// team being read and <c>viewer</c> is the person reading it — that is
    /// exactly the case this whole class exists to get right.
    /// </summary>
    public static string? ResolveViewer(
        string? path, string? routeUsername, string? queryUsername, string? queryViewer)
    {
        if (!string.IsNullOrWhiteSpace(queryViewer)) return queryViewer;
        if (!string.IsNullOrWhiteSpace(queryUsername)) return queryUsername;

        if (!string.IsNullOrWhiteSpace(routeUsername)
            && path is not null
            && path.StartsWith(SelfAddressed, StringComparison.OrdinalIgnoreCase))
            return routeUsername;

        // A league-scoped team route with no viewer in the query — season
        // stats, picks, a player's week-by-week. These are read about other
        // people as often as about oneself, so they say nothing. Missing a
        // stamp costs a stale label for at most one more request; guessing
        // wrong invents activity that never happened.
        return null;
    }
}
