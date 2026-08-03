using FantasyWarrior.Core.Messaging;

namespace FantasyWarrior.Core.Tests.Messaging;

public class PresenceTests
{
    private static readonly DateTime Now = new(2026, 8, 3, 20, 0, 0, DateTimeKind.Utc);

    private static DateTime AgoMinutes(double minutes) => Now.AddMinutes(-minutes);

    private static MemberSeen Seen(int id, string name, double? minutesAgo = null) =>
        new(id, name, minutesAgo is null ? null : AgoMinutes(minutesAgo.Value));

    // --- online is exactly "connected", with nothing layered on top ---

    [Fact]
    public void For_ReportsOnline_WhenConnected()
    {
        var m = Presence.For(Seen(1, "bob", 45), isConnected: true, Now);
        Assert.True(m.Online);
        Assert.Equal("Online", m.Label);
    }

    [Fact]
    public void For_ReportsOffline_WhenDisconnected_HoweverRecentlySeen()
    {
        // No grace window: recent activity words the label, it never lights the
        // dot. The old 90s window is exactly what forced the offline push to be
        // delayed past it, and then to disagree with it.
        var m = Presence.For(Seen(1, "bob", 0.2), isConnected: false, Now);
        Assert.False(m.Online);
    }

    [Fact]
    public void For_ReportsOnline_WhenConnectedAndNeverSeenBefore()
    {
        // A brand new user connects before any request has stamped LastSeenUtc.
        Assert.True(Presence.For(Seen(1, "bob"), isConnected: true, Now).Online);
    }

    [Fact]
    public void For_CarriesLastSeenThrough_Unchanged()
    {
        var seen = AgoMinutes(200);
        Assert.Equal(seen, Presence.For(new MemberSeen(1, "bob", seen), false, Now).LastSeenUtc);
    }

    [Fact]
    public void For_LabelsSomeoneNeverSeen_WithADash()
    {
        Assert.Equal("—", Presence.For(Seen(1, "bob"), isConnected: false, Now).Label);
    }

    // --- Roster ---

    [Fact]
    public void Roster_ReturnsEveryMember_IncludingThoseWhoAreAway()
    {
        // The whole point of a roster over a delta: it is the complete truth,
        // so a client that has just connected learns about everyone.
        var roster = Presence.Roster(
            [Seen(1, "bob", 5), Seen(2, "ann", 5), Seen(3, "cid", 5)],
            id => id == 1,
            Now);

        Assert.Equal(3, roster.Count);
    }

    [Fact]
    public void Roster_PutsOnlineMembersFirst()
    {
        var roster = Presence.Roster(
            [Seen(1, "zed", 5), Seen(2, "ann", 5)],
            id => id == 1,
            Now);

        Assert.Equal("zed", roster[0].Username);
        Assert.True(roster[0].Online);
    }

    [Fact]
    public void Roster_OrdersOfflineMembersByMostRecentlySeen()
    {
        var roster = Presence.Roster(
            [Seen(1, "old", 500), Seen(2, "recent", 5), Seen(3, "never")],
            _ => false,
            Now);

        Assert.Equal(["recent", "old", "never"], roster.Select(m => m.Username));
    }

    [Fact]
    public void Roster_IsDeterministic_WhenTwoMembersTie()
    {
        var members = new[] { Seen(1, "zed", 5), Seen(2, "ann", 5) };

        Assert.Equal(["ann", "zed"], Presence.Roster(members, _ => false, Now).Select(m => m.Username));
        Assert.Equal(
            ["ann", "zed"],
            Presence.Roster(members.Reverse(), _ => false, Now).Select(m => m.Username));
    }

    [Fact]
    public void Roster_IsIdempotent_ForTheSameInputs()
    {
        var members = new[] { Seen(1, "bob", 5), Seen(2, "ann") };
        Func<int, bool> connected = id => id == 1;

        Assert.Equal(
            Presence.Roster(members, connected, Now),
            Presence.Roster(members, connected, Now));
    }

    [Fact]
    public void Roster_ReturnsNothing_ForAnEmptyLeague()
    {
        Assert.Empty(Presence.Roster([], _ => false, Now));
    }

    // --- Describe ---

    [Theory]
    [InlineData(0.2, "just now")]
    [InlineData(5, "5min ago")]
    [InlineData(59, "59min ago")]
    [InlineData(60, "1h ago")]
    [InlineData(60 * 23, "23h ago")]
    [InlineData(60 * 24, "1d ago")]
    [InlineData(60 * 24 * 29, "29d ago")]
    [InlineData(60 * 24 * 40, "a while ago")]
    public void Describe_PicksTheCoarsestUnitThatStillReads(double minutesAgo, string expected)
    {
        Assert.Equal(expected, Presence.Describe(online: false, Now.AddMinutes(-minutesAgo), Now));
    }

    [Fact]
    public void Describe_SaysJustNow_WhenTheStampIsSlightlyInTheFuture()
    {
        // Clock skew between the API and the database is real; a negative
        // "-1m ago" is not an acceptable way to surface it.
        Assert.Equal("just now", Presence.Describe(online: false, Now.AddSeconds(20), Now));
    }

    [Fact]
    public void Describe_IgnoresLastSeen_WhenOnline()
    {
        Assert.Equal("Online", Presence.Describe(online: true, AgoMinutes(500), Now));
    }
}
