using FantasyWarrior.Core.Messaging;
using FantasyWarrior.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>What the client renders for one GM's presence.</summary>
/// <summary>Field names match the conversation-list row in MessageEndpoints, so
/// the client can apply a pushed update straight onto a fetched row.</summary>
public sealed record PresencePayload(string Username, bool Online, DateTime? LastSeenUtc, string PresenceLabel);

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
/// TEMPORARY AUTH MODEL: the username arrives in the query string and is
/// trusted, exactly like the REST API. The stakes are higher here than
/// elsewhere, because what it guards is private messages rather than a lineup —
/// see the open items in project_status.md.
/// </summary>
public sealed class LiveHub(
    FantasyWarriorDbContext db,
    PresenceRegistry presence,
    IHubContext<LiveHub> hub,
    IServiceScopeFactory scopeFactory,
    ILogger<LiveHub> log) : Hub
{
    /// <summary>
    /// How long to wait before telling a league someone went offline.
    ///
    /// Disconnects happen for reasons that are not "they left": every deploy
    /// swaps the revision and drops all sockets at once, phones change
    /// networks, laptops sleep for a second. Announcing immediately would blink
    /// every dot in the league on each of those.
    ///
    /// <b>Derived from the grace window rather than picked.</b> Until
    /// <see cref="Presence.GraceWindow"/> has elapsed, <c>Presence.Resolve</c>
    /// still reports someone as online — so announcing sooner would push
    /// "offline" while every fetch of the same list said "online", and the dot
    /// would change on refresh. Waiting past the window is what makes the
    /// pushed answer and the fetched one identical.
    /// </summary>
    private static readonly TimeSpan OfflineDebounce = Presence.GraceWindow + TimeSpan.FromSeconds(5);

    private const string UserIdKey = "userId";
    private const string UsernameKey = "username";
    private const string LeagueGroupKey = "leagueGroup";

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
        Context.Items[UsernameKey] = user.Username;

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(user.UserId));

        // The league group is how presence reaches the other GMs. The client
        // reconnects with a new league code when the user switches pools, so a
        // connection only ever belongs to the one being looked at.
        if (!string.IsNullOrWhiteSpace(leagueCode))
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueCode);
            if (league is not null)
            {
                var group = LeagueGroup(league.LeagueId);
                Context.Items[LeagueGroupKey] = group;
                await Groups.AddToGroupAsync(Context.ConnectionId, group);
            }
        }

        var now = DateTime.UtcNow;
        try
        {
            await presence.StampAsync(db, user.Username, now);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not stamp LastSeenUtc for {Username} on connect.", user.Username);
        }

        var isFirst = presence.Connect(user.UserId);
        if (isFirst) await BroadcastAsync(user.Username, online: true, now);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(UserIdKey, out var raw) && raw is int userId)
        {
            var username = Context.Items[UsernameKey] as string ?? string.Empty;
            var group = Context.Items[LeagueGroupKey] as string;

            if (presence.Disconnect(userId) && group is not null)
            {
                // Detached on purpose: the hub instance and its scoped
                // DbContext are gone by the time this fires, which is why it
                // broadcasts through IHubContext and reads nothing.
                _ = AnnounceOfflineAfterDebounceAsync(userId, username, group);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task AnnounceOfflineAfterDebounceAsync(int userId, string username, string group)
    {
        try
        {
            await Task.Delay(OfflineDebounce);

            // Reconnected in the meantime — the dot never should have moved.
            if (presence.IsOnline(userId)) return;

            // Re-read rather than assume the disconnect time: someone whose
            // socket died while the app kept working still has fresh REST
            // traffic, and that has to count. Its own scope, because the hub
            // instance and its DbContext are long gone by now.
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FantasyWarriorDbContext>();
            var lastSeen = await db.Users
                .Where(u => u.UserId == userId)
                .Select(u => u.LastSeenUtc)
                .FirstOrDefaultAsync();

            var state = Presence.Resolve(isConnected: false, lastSeen, DateTime.UtcNow);

            // Still reads as online — nothing changed, so say nothing.
            if (state.Online) return;

            await hub.Clients.Group(group).SendAsync(
                "presence", new PresencePayload(username, state.Online, state.LastSeenUtc, state.Label));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not announce {Username} going offline.", username);
        }
    }

    private async Task BroadcastAsync(string username, bool online, DateTime nowUtc)
    {
        if (Context.Items[LeagueGroupKey] is not string group) return;

        var state = Presence.Resolve(online, nowUtc, nowUtc);
        await Clients.Group(group).SendAsync(
            "presence", new PresencePayload(username, state.Online, state.LastSeenUtc, state.Label));
    }
}
