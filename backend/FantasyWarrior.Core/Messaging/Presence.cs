namespace FantasyWarrior.Core.Messaging;

/// <summary>How a GM's presence reads to everyone else.</summary>
/// <param name="Online">Whether to light the green dot.</param>
/// <param name="LastSeenUtc">Null for someone who has never been seen at all.</param>
/// <param name="Label">Ready to render: "Online", "12m ago", "3d ago", "—".</param>
public sealed record PresenceState(bool Online, DateTime? LastSeenUtc, string Label);

/// <summary>
/// Presence in two layers.
///
/// The live layer is the SignalR connection registry — an open connection is
/// the only thing that actually proves someone is there right now. The durable
/// layer is <c>User.LastSeenUtc</c>, stamped by the API's presence middleware,
/// which is what still has an answer for the fourteen GMs who are not connected.
///
/// The grace window is what stops the two from contradicting each other. A
/// connection drops for reasons that have nothing to do with the person: a
/// revision swap on deploy, a phone changing towers, the client's own idle
/// timeout firing a second before they touch the screen again. Anyone whose
/// REST traffic is that recent is still here, whatever the socket says.
/// </summary>
public static class Presence
{
    /// <summary>How stale <c>LastSeenUtc</c> may be before a disconnected user reads as away.</summary>
    public static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(90);

    public static PresenceState Resolve(bool isConnected, DateTime? lastSeenUtc, DateTime nowUtc)
    {
        var online = isConnected
            || (lastSeenUtc is not null && nowUtc - lastSeenUtc.Value <= GraceWindow);

        return new PresenceState(online, lastSeenUtc, Describe(online, lastSeenUtc, nowUtc));
    }

    /// <summary>
    /// The label the UI renders. It lives here rather than in the frontend so
    /// there is one implementation of it under CI, instead of a second
    /// hand-rolled timeAgo drifting away from this one.
    ///
    /// Deliberately the *short* form ("45min ago"), not "last seen 45min ago":
    /// the profile menu spells that out because it has the width, while the
    /// chat list uses the same string as a right-aligned stamp where the prefix
    /// would not fit. One wording, two framings.
    /// </summary>
    public static string Describe(bool online, DateTime? lastSeenUtc, DateTime nowUtc)
    {
        if (online) return "Online";
        if (lastSeenUtc is null) return "—";

        var elapsed = nowUtc - lastSeenUtc.Value;

        // A clock skew between the API and the database can put LastSeenUtc a
        // little in the future; "just now" is a better answer than a negative
        // number of minutes.
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes}min ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed < TimeSpan.FromDays(30)) return $"{(int)elapsed.TotalDays}d ago";
        return "a while ago";
    }
}
