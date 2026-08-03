using FantasyWarrior.Core.Messaging;
using FantasyWarrior.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>Who is online in a league, right now. The entire presence payload.</summary>
public sealed record OnlinePayload(string LeagueId, IReadOnlyList<string> Online);

/// <summary>
/// The app's one live channel, and it does exactly two things: tell a league who
/// is online, and deliver private messages between its GMs.
///
/// <b>Push only.</b> No client-callable method. Sending a message is a POST to
/// <c>MessageEndpoints</c>, which persists it and then pushes through
/// <see cref="IHubContext{LiveHub}"/> — one write path, so one place validates
/// and one place stores. A hub method would be more idiomatic SignalR and would
/// save a hop, but it can only be called while connected, and the client
/// deliberately drops its connection whenever the tab is hidden.
///
/// <b>Presence is a list of who is online, never a delta.</b> Sending "nick just
/// arrived" tells the league something it can apply but tells *nick* nothing
/// about the five people already there. Sending the set is idempotent, repairs a
/// missed event on the next change, and reaches the arriving client through the
/// same group as everyone else.
///
/// <b>No database in the broadcast path.</b> <see cref="PresenceService"/>
/// already knows who is connected; the labels for people who are *not* online
/// come from the REST list instead, because they change slowly and nobody needs
/// them to the second.
///
/// TEMPORARY AUTH MODEL: the username arrives in the query string and is
/// trusted, exactly like the REST API. The stakes are higher here than
/// elsewhere, because what it guards is private messages rather than a lineup —
/// see the open items in project_status.md.
/// </summary>
public sealed class LiveHub(
    FantasyWarriorDbContext db,
    PresenceService presence,
    LastSeenStamper lastSeen,
    ILogger<LiveHub> log) : Hub
{
    public static string UserGroup(int userId) => $"u:{userId}";
    public static string LeagueGroup(int leagueId) => $"league:{leagueId}";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var rawUsername = http?.Request.Query["username"].ToString();
        var leagueCode = http?.Request.Query["league"].ToString();

        // Without both, this connection can neither receive messages nor appear
        // in a presence list. Abort rather than hold a socket open that does
        // nothing but keep the replica awake.
        if (string.IsNullOrWhiteSpace(rawUsername) || string.IsNullOrWhiteSpace(leagueCode))
        {
            Context.Abort();
            return;
        }

        var username = Queries.Normalize(rawUsername);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
        var league = await Queries.LeagueByCodeAsync(db, leagueCode);
        if (user is null || league is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(user.UserId));
        await Groups.AddToGroupAsync(Context.ConnectionId, LeagueGroup(league.LeagueId));

        try
        {
            await lastSeen.StampAsync(db, user.Username, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not stamp LastSeenUtc for {Username} on connect.", user.Username);
        }

        presence.Join(Context.ConnectionId, user.UserId, user.Username, league.LeagueId, league.JoinCode);

        // Unconditional, even on a user's second tab where the set has not
        // changed: that new connection is the one that needs to be told.
        await BroadcastAsync(league.LeagueId, league.JoinCode);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var (leagueId, leagueCode, changed) = presence.Leave(Context.ConnectionId);

        // Not a single query on this path: the service carried the join code in
        // from the connect, so a disconnect is pure memory plus one send.
        if (leagueId is not null && changed)
            await BroadcastAsync(leagueId.Value, leagueCode ?? string.Empty);

        await base.OnDisconnectedAsync(exception);
    }

    private async Task BroadcastAsync(int leagueId, string leagueCode)
    {
        try
        {
            await Clients.Group(LeagueGroup(leagueId))
                .SendAsync("presence", new OnlinePayload(leagueCode, presence.OnlineUsernames(leagueId)));
        }
        catch (Exception ex)
        {
            // A dropped broadcast repairs itself on the next connect or
            // disconnect, so it is never worth failing a connection over.
            log.LogWarning(ex, "Could not broadcast presence for league {LeagueId}.", leagueId);
        }
    }
}

/// <summary>
/// A league's members with their presence and their "last seen" wording, for
/// the REST list. The push does not use this — it carries only who is online,
/// which needs no database at all.
/// </summary>
public static class PresenceRoster
{
    public static async Task<IReadOnlyList<MemberPresence>> ForLeagueAsync(
        FantasyWarriorDbContext db, PresenceService presence, int leagueId, CancellationToken ct = default)
    {
        var members = await db.LeagueMembers
            .Where(m => m.LeagueId == leagueId)
            .Select(m => new MemberSeen(m.UserId, m.User!.Username, m.User.LastSeenUtc))
            .ToListAsync(ct);

        return Presence.Roster(members, userId => presence.IsOnline(leagueId, userId), DateTime.UtcNow);
    }
}
