using FantasyWarrior.Core.Messaging;
using FantasyWarrior.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>
/// The league's presence, whole. One shape, whether it was pushed or fetched.
/// </summary>
public sealed record RosterPayload(string LeagueId, IReadOnlyList<MemberPresence> Members);

/// <summary>
/// The app's one live channel. Named <c>Live</c> rather than <c>Chat</c> because
/// it already carries presence, and the <c>notice</c> event exists for the
/// event pop-ups that will follow — a <c>ChatHub</c> would have become a lie on
/// the first one.
///
/// <b>Push only.</b> The hub exposes no client-callable method: sending a
/// message is a POST to <c>MessageEndpoints</c>, which persists it and then
/// pushes through <see cref="IHubContext{LiveHub}"/>. One write path means one
/// place that validates and one place that stores, instead of two that have to
/// agree — the same reasoning as awarding cockcoin inline at the earning action.
///
/// <b>Presence is broadcast as a roster, never as a delta</b> (2026-08-03).
/// Sending "nick just arrived" tells the league something it can apply, but
/// tells *nick* nothing about the five people already there — his own counter
/// would read zero until some REST call happened to seed it. Sending the whole
/// list on every change costs a few hundred bytes for fourteen GMs, reaches the
/// arriving client through the same group as everyone else, and is idempotent:
/// a missed event is repaired by the next one instead of leaving a dot stuck.
///
/// TEMPORARY AUTH MODEL: the username arrives in the query string and is
/// trusted, exactly like the REST API. The stakes are higher here than
/// elsewhere, because what it guards is private messages rather than a lineup —
/// see the open items in project_status.md.
/// </summary>
public sealed class LiveHub(
    FantasyWarriorDbContext db,
    PresenceRegistry presence,
    ILogger<LiveHub> log) : Hub
{
    private const string UserIdKey = "userId";
    private const string LeagueIdKey = "leagueId";
    private const string LeagueCodeKey = "leagueCode";

    public static string UserGroup(int userId) => $"u:{userId}";
    public static string LeagueGroup(int leagueId) => $"league:{leagueId}";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var rawUsername = http?.Request.Query["username"].ToString();
        var leagueCode = http?.Request.Query["league"].ToString();

        if (string.IsNullOrWhiteSpace(rawUsername))
        {
            // Nothing to attach the connection to. Abort rather than hold an
            // anonymous socket open, which would keep the replica awake for a
            // client that can never receive anything.
            Context.Abort();
            return;
        }

        var username = Queries.Normalize(rawUsername);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null)
        {
            Context.Abort();
            return;
        }

        Context.Items[UserIdKey] = user.UserId;
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(user.UserId));

        // The league group is how presence reaches the other GMs. The client
        // reconnects with a new league code when the user switches pools, so a
        // connection only ever belongs to the one being looked at.
        if (!string.IsNullOrWhiteSpace(leagueCode))
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueCode);
            if (league is not null)
            {
                Context.Items[LeagueIdKey] = league.LeagueId;
                Context.Items[LeagueCodeKey] = league.JoinCode;
                await Groups.AddToGroupAsync(Context.ConnectionId, LeagueGroup(league.LeagueId));
            }
        }

        try
        {
            await presence.StampAsync(db, user.Username, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not stamp LastSeenUtc for {Username} on connect.", user.Username);
        }

        presence.Connect(user.UserId);

        // Unconditional, even when this is the user's second tab and the roster
        // has not changed: the new connection is the one that needs it.
        await BroadcastRosterAsync();

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(UserIdKey, out var raw) && raw is int userId
            && presence.Disconnect(userId))
        {
            // Plainly awaited, inside the scope SignalR gives this callback.
            // The previous version deferred this behind a timer and had to
            // rebuild a DI scope to serve it; with no grace window left to wait
            // out, there is nothing to defer.
            await BroadcastRosterAsync();
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task BroadcastRosterAsync()
    {
        if (Context.Items[LeagueIdKey] is not int leagueId) return;
        var code = Context.Items[LeagueCodeKey] as string ?? string.Empty;

        try
        {
            var members = await PresenceRoster.ForLeagueAsync(db, presence, leagueId);
            await Clients.Group(LeagueGroup(leagueId))
                .SendAsync("presence", new RosterPayload(code, members));
        }
        catch (Exception ex)
        {
            // A dropped roster repairs itself on the next connect or
            // disconnect, so this is never worth failing a connection over.
            log.LogWarning(ex, "Could not broadcast the roster for league {LeagueId}.", leagueId);
        }
    }
}

/// <summary>
/// The one place a league's presence is assembled from the database, shared by
/// the hub's push and the REST fetch so the two cannot answer differently.
/// </summary>
public static class PresenceRoster
{
    public static async Task<IReadOnlyList<MemberPresence>> ForLeagueAsync(
        FantasyWarriorDbContext db, PresenceRegistry presence, int leagueId, CancellationToken ct = default)
    {
        var members = await db.LeagueMembers
            .Where(m => m.LeagueId == leagueId)
            .Select(m => new MemberSeen(m.UserId, m.User!.Username, m.User.LastSeenUtc))
            .ToListAsync(ct);

        return Presence.Roster(members, presence.IsOnline, DateTime.UtcNow);
    }
}
