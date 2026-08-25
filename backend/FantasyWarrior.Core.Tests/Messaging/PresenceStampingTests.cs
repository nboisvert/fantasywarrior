using FantasyWarrior.Core.Messaging;

namespace FantasyWarrior.Core.Tests.Messaging;

/// <summary>
/// The regression these lock down is the 2026-08-25 one: browsing a rival's
/// team marked the rival present. Every "Viewed" case below returned the wrong
/// person before the fix.
/// </summary>
public class PresenceStampingTests
{
    private static string? Resolve(
        string path, string? route = null, string? username = null, string? viewer = null) =>
        PresenceStamping.ResolveViewer(path, route, username, viewer);

    // --- the bug: the route segment names the team, not the caller ---

    [Fact]
    public void Ignores_RouteUsername_OnALeagueTeamRoute()
    {
        // GET /api/leagues/TKW6UR/teams/steeve/season-stats — Nick pricing a
        // trade against Steeve. Nothing here proves Steeve is anywhere.
        Assert.Null(Resolve("/api/leagues/TKW6UR/teams/steeve/season-stats", route: "steeve"));
    }

    [Fact]
    public void Ignores_RouteUsername_OnPicks()
    {
        Assert.Null(Resolve("/api/leagues/TKW6UR/teams/christian/picks", route: "christian"));
    }

    [Fact]
    public void Ignores_RouteUsername_OnPlayerPeriods()
    {
        Assert.Null(Resolve("/api/leagues/TKW6UR/teams/steeve/players/8471675/periods", route: "steeve"));
    }

    [Fact]
    public void PrefersViewer_OverTheTeamBeingRead()
    {
        // GET /api/leagues/TKW6UR/teams/steeve/lineup?viewer=nick
        Assert.Equal("nick", Resolve("/api/leagues/TKW6UR/teams/steeve/lineup", route: "steeve", viewer: "nick"));
    }

    [Fact]
    public void PrefersViewer_EvenWhenAUsernameQueryIsAlsoPresent()
    {
        Assert.Equal("nick", Resolve("/api/leagues/TKW6UR/teams/steeve/lineup",
            route: "steeve", username: "steeve", viewer: "nick"));
    }

    // --- the query string is the viewer everywhere else ---

    [Fact]
    public void UsesUsernameQuery_OnLeagueRoutes()
    {
        Assert.Equal("nick", Resolve("/api/leagues/TKW6UR/trades", username: "nick"));
    }

    [Fact]
    public void UsesUsernameQuery_OnAThreadRouteWhosePeerIsRouted()
    {
        // The peer segment is called "peer", not "username", so it never
        // competed — but the viewer still has to come from the query.
        Assert.Equal("nick", Resolve("/api/leagues/TKW6UR/messages/steeve", username: "nick"));
    }

    // --- the one family where the routed subject IS the caller ---

    [Fact]
    public void TrustsRouteUsername_OnTheUsersFamily()
    {
        // The first call the app makes after login. Without this, a user who
        // signs in and lands on the league gate would read as never seen.
        Assert.Equal("steeve", Resolve("/api/users/steeve/leagues", route: "steeve"));
    }

    [Fact]
    public void TrustsRouteUsername_OnUsersFamily_CaseInsensitively()
    {
        Assert.Equal("steeve", Resolve("/API/Users/steeve/cockcoin", route: "steeve"));
    }

    // --- nothing to say ---

    [Fact]
    public void ReturnsNull_WhenNothingNamesAnyone()
    {
        Assert.Null(Resolve("/api/leagues/TKW6UR/free-agents"));
    }

    [Fact]
    public void ReturnsNull_ForBlankValues()
    {
        Assert.Null(Resolve("/api/users/x/leagues", route: "   ", username: "", viewer: "  "));
    }

    [Fact]
    public void ReturnsNull_WhenPathIsUnknown()
    {
        Assert.Null(Resolve(null!, route: "steeve"));
    }
}
