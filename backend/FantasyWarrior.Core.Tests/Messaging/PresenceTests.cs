using FantasyWarrior.Core.Messaging;

namespace FantasyWarrior.Core.Tests.Messaging;

public class PresenceTests
{
    private static readonly DateTime Now = new(2026, 8, 3, 20, 0, 0, DateTimeKind.Utc);

    private static DateTime AgoMinutes(double minutes) => Now.AddMinutes(-minutes);

    // --- the live layer ---

    [Fact]
    public void Resolve_ReportsOnline_WhenConnected()
    {
        var state = Presence.Resolve(isConnected: true, lastSeenUtc: AgoMinutes(45), Now);
        Assert.True(state.Online);
        Assert.Equal("Online", state.Label);
    }

    [Fact]
    public void Resolve_ReportsOnline_WhenConnectedAndNeverSeenBefore()
    {
        // A brand new user connects before any request has stamped LastSeenUtc.
        var state = Presence.Resolve(isConnected: true, lastSeenUtc: null, Now);
        Assert.True(state.Online);
    }

    // --- the durable layer and the grace window ---

    [Fact]
    public void Resolve_ReportsOnline_WhenDisconnectedButSeenInsideTheGraceWindow()
    {
        // A deploy swaps the revision and drops every socket. Someone whose
        // REST traffic is seconds old has not left.
        var state = Presence.Resolve(isConnected: false, lastSeenUtc: AgoMinutes(1), Now);
        Assert.True(state.Online);
    }

    [Fact]
    public void Resolve_ReportsAway_WhenDisconnectedAndPastTheGraceWindow()
    {
        var state = Presence.Resolve(
            isConnected: false, lastSeenUtc: Now - Presence.GraceWindow.Add(TimeSpan.FromSeconds(1)), Now);
        Assert.False(state.Online);
    }

    [Fact]
    public void Resolve_ReportsAway_AfterTheClientsThreeMinuteIdleTimeout()
    {
        // The frontend drops the connection after 3 min without interaction.
        // Presence has to agree with that, or the dot lies about who is around.
        var state = Presence.Resolve(isConnected: false, lastSeenUtc: AgoMinutes(3), Now);
        Assert.False(state.Online);
        Assert.Equal("3min ago", state.Label);
    }

    [Fact]
    public void Resolve_ReportsAway_WhenNeverSeenAndNotConnected()
    {
        var state = Presence.Resolve(isConnected: false, lastSeenUtc: null, Now);
        Assert.False(state.Online);
        Assert.Equal("—", state.Label);
    }

    [Fact]
    public void Resolve_CarriesLastSeenThrough_Unchanged()
    {
        var seen = AgoMinutes(200);
        Assert.Equal(seen, Presence.Resolve(false, seen, Now).LastSeenUtc);
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
}
