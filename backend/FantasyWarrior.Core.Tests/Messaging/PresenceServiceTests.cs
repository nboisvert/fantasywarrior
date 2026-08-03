using FantasyWarrior.Core.Messaging;

namespace FantasyWarrior.Core.Tests.Messaging;

public class PresenceServiceTests
{
    private const int Mordus = 1;
    private const int OtherPool = 2;

    private const int Nick = 10;
    private const int Bob = 11;

    // --- joining ---

    [Fact]
    public void Join_ReportsAChange_ForAUsersFirstConnection()
    {
        var presence = new PresenceService();
        Assert.True(presence.Join("c1", Nick, "nick", Mordus, "CODE"));
    }

    [Fact]
    public void Join_ReportsNoChange_ForASecondTab()
    {
        // Nothing new to tell the league, so nothing should be broadcast on
        // its account.
        var presence = new PresenceService();
        presence.Join("c1", Nick, "nick", Mordus, "CODE");
        Assert.False(presence.Join("c2", Nick, "nick", Mordus, "CODE"));
    }

    [Fact]
    public void Join_ListsTheUserOnce_HoweverManyTabsAreOpen()
    {
        var presence = new PresenceService();
        presence.Join("c1", Nick, "nick", Mordus, "CODE");
        presence.Join("c2", Nick, "nick", Mordus, "CODE");

        Assert.Equal(["nick"], presence.OnlineUsernames(Mordus));
    }

    // --- leaving ---

    [Fact]
    public void Leave_KeepsTheUserOnline_WhenAnotherTabIsStillOpen()
    {
        // The phone-and-laptop case: closing one must not put the dot out.
        var presence = new PresenceService();
        presence.Join("phone", Nick, "nick", Mordus, "CODE");
        presence.Join("laptop", Nick, "nick", Mordus, "CODE");

        var (leagueId, leagueCode, changed) = presence.Leave("phone");

        Assert.Equal(Mordus, leagueId);
        Assert.Equal("CODE", leagueCode);
        Assert.False(changed);
        Assert.True(presence.IsOnline(Mordus, Nick));
    }

    [Fact]
    public void Leave_TakesTheUserOffline_OnTheLastConnection()
    {
        var presence = new PresenceService();
        presence.Join("phone", Nick, "nick", Mordus, "CODE");
        presence.Join("laptop", Nick, "nick", Mordus, "CODE");
        presence.Leave("phone");

        var (leagueId, leagueCode, changed) = presence.Leave("laptop");

        Assert.Equal(Mordus, leagueId);
        // Carried in from the connect, so the disconnect broadcast can be
        // labelled without a database lookup.
        Assert.Equal("CODE", leagueCode);
        Assert.True(changed);
        Assert.False(presence.IsOnline(Mordus, Nick));
        Assert.Empty(presence.OnlineUsernames(Mordus));
    }

    [Fact]
    public void Leave_IsHarmless_ForAConnectionItNeverSaw()
    {
        // An aborted handshake still reaches OnDisconnectedAsync, and SignalR
        // can deliver a disconnect twice; neither may throw or broadcast.
        var presence = new PresenceService();
        presence.Join("c1", Nick, "nick", Mordus, "CODE");

        var (leagueId, _, changed) = presence.Leave("never-registered");

        Assert.Null(leagueId);
        Assert.False(changed);
        Assert.True(presence.IsOnline(Mordus, Nick));
    }

    [Fact]
    public void Leave_IsHarmless_WhenTheSameConnectionLeavesTwice()
    {
        var presence = new PresenceService();
        presence.Join("c1", Nick, "nick", Mordus, "CODE");
        presence.Leave("c1");

        var (leagueId, _, changed) = presence.Leave("c1");

        Assert.Null(leagueId);
        Assert.False(changed);
    }

    // --- leagues are independent ---

    [Fact]
    public void Leagues_DoNotSeeEachOthersConnections()
    {
        // Presence is league-scoped on purpose: a connection carries one
        // league, so someone looking at another pool is not "here".
        var presence = new PresenceService();
        presence.Join("c1", Nick, "nick", Mordus, "CODE");

        Assert.True(presence.IsOnline(Mordus, Nick));
        Assert.False(presence.IsOnline(OtherPool, Nick));
        Assert.Empty(presence.OnlineUsernames(OtherPool));
    }

    [Fact]
    public void Leaving_OneLeague_LeavesTheOtherAlone()
    {
        // The same person with both pools open in two tabs.
        var presence = new PresenceService();
        presence.Join("c1", Nick, "nick", Mordus, "CODE");
        presence.Join("c2", Nick, "nick", OtherPool, "CODE");

        presence.Leave("c1");

        Assert.False(presence.IsOnline(Mordus, Nick));
        Assert.True(presence.IsOnline(OtherPool, Nick));
    }

    // --- the payload ---

    [Fact]
    public void OnlineUsernames_ReturnsNothing_ForALeagueNobodyHasJoined()
    {
        Assert.Empty(new PresenceService().OnlineUsernames(Mordus));
    }

    [Fact]
    public void OnlineUsernames_IsSorted_SoTwoIdenticalStatesProduceTheSameArray()
    {
        // The client replaces its whole set from this; a stable order keeps it
        // from re-rendering over nothing.
        var a = new PresenceService();
        a.Join("c1", Nick, "nick", Mordus, "CODE");
        a.Join("c2", Bob, "bob", Mordus, "CODE");

        var b = new PresenceService();
        b.Join("c1", Bob, "bob", Mordus, "CODE");
        b.Join("c2", Nick, "nick", Mordus, "CODE");

        Assert.Equal(["bob", "nick"], a.OnlineUsernames(Mordus));
        Assert.Equal(a.OnlineUsernames(Mordus), b.OnlineUsernames(Mordus));
    }

    [Fact]
    public void IsOnline_IsFalse_ForSomeoneWhoNeverConnected()
    {
        var presence = new PresenceService();
        presence.Join("c1", Nick, "nick", Mordus, "CODE");

        Assert.False(presence.IsOnline(Mordus, Bob));
    }
}
