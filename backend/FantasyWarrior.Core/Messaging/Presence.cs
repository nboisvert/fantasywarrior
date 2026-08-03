namespace FantasyWarrior.Core.Messaging;

/// <summary>One league member as everyone else sees them.</summary>
/// <param name="Online">Whether to light the green dot.</param>
/// <param name="LastSeenUtc">Null for someone who has never been seen at all.</param>
/// <param name="Label">Ready to render: "Online", "45min ago", "3d ago", "—".</param>
public sealed record MemberPresence(string Username, bool Online, DateTime? LastSeenUtc, string Label);

/// <summary>A league member as the database holds them, before presence is applied.</summary>
public sealed record MemberSeen(int UserId, string Username, DateTime? LastSeenUtc);

/// <summary>
/// Who is in a league and who is there right now.
///
/// <b>Online means one thing: a live connection.</b> There is no "recently
/// active so probably still around" window, and that is a deliberate
/// simplification (2026-08-03). The earlier version had one, which forced the
/// offline announcement to be delayed past it, which forced a detached timer,
/// which then disagreed with the window it was built around. One predicate
/// removes that whole class of bug — and with the client holding its
/// connection only while the app is actually being used, "connected" and
/// "here" mean the same thing anyway.
///
/// <c>LastSeenUtc</c> survives, but only to word the label for people who are
/// *not* online. It never decides the dot.
/// </summary>
public static class Presence
{
    /// <summary>
    /// The whole league in one shot, which is the only thing ever sent or
    /// fetched. A roster is idempotent where a per-person delta is not: apply
    /// it twice and nothing changes, miss one and the next arrival repairs it,
    /// and a client that has just connected learns about everyone already
    /// there rather than only about itself.
    ///
    /// Ordered online-first, then most recently seen, then alphabetically —
    /// once, here, so no caller has to re-derive it and drift.
    /// </summary>
    public static IReadOnlyList<MemberPresence> Roster(
        IEnumerable<MemberSeen> members, Func<int, bool> isConnected, DateTime nowUtc) =>
        members
            .Select(m => For(m, isConnected(m.UserId), nowUtc))
            .OrderByDescending(m => m.Online)
            .ThenByDescending(m => m.LastSeenUtc ?? DateTime.MinValue)
            .ThenBy(m => m.Username, StringComparer.Ordinal)
            .ToList();

    public static MemberPresence For(MemberSeen member, bool isConnected, DateTime nowUtc) =>
        new(member.Username, isConnected, member.LastSeenUtc,
            Describe(isConnected, member.LastSeenUtc, nowUtc));

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
