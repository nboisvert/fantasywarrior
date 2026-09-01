using FantasyWarrior.Core.Cockcoin;
using FantasyWarrior.Core.Messaging;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>
/// Direct messages between the GMs of one league, and the presence that sits
/// beside them in the same list.
///
/// Threads are per league on purpose — see <see cref="Message"/>. Every route
/// here therefore resolves the league first and refuses anyone who is not a
/// member of it: a pool's conversations are not readable from another pool, and
/// membership is the only check there is until real auth lands.
/// </summary>
public static class MessageEndpoints
{
    /// <summary>How many messages a thread returns. Older ones are not reachable yet.</summary>
    private const int ThreadLimit = 50;

    public static void Map(WebApplication app)
    {
        // The conversation list: one row per other GM in the league, whether or
        // not anything has ever been said. A pool of fourteen is a contact list
        // and an inbox at once, so splitting them into two screens would only
        // make starting a conversation harder.
        app.MapGet("/api/leagues/{leagueId}/messages", async (
            string leagueId, string username, FantasyWarriorDbContext db, PresenceService presence) =>
        {
            var context = await ResolveAsync(db, leagueId, username);
            if (context.Error is not null) return context.Error;
            var (league, me) = (context.League!, context.Me!);

            // Presence comes from the shared roster builder, the same one the
            // hub pushes, so a fetched dot and a pushed dot can never disagree.
            var roster = await PresenceRoster.ForLeagueAsync(db, presence, league.LeagueId);
            // Projected in the query, not in ToDictionary's selectors: those run
            // client-side over materialized LeagueMember rows, where User is
            // null because nothing asked SQL to join it.
            var peerIds = await db.LeagueMembers
                .Where(m => m.LeagueId == league.LeagueId)
                .Select(m => new { m.UserId, m.User!.Username })
                .ToDictionaryAsync(x => x.Username, x => x.UserId);

            var rows = await db.Messages.AsNoTracking()
                .Where(m => m.LeagueId == league.LeagueId
                            && (m.SenderUserId == me.UserId || m.RecipientUserId == me.UserId))
                .Select(m => new MessageRow(
                    m.MessageId, m.SenderUserId, m.RecipientUserId, m.Body, m.SentUtc, m.ReadUtc))
                .ToListAsync();

            var conversations = ConversationSummary.Build(rows, me.UserId).ToDictionary(c => c.PeerUserId);

            var contacts = roster
                .Where(p => p.Username != me.Username)
                .Select(p =>
                {
                    Conversation? c = null;
                    if (peerIds.TryGetValue(p.Username, out var peerId))
                        conversations.TryGetValue(peerId, out c);

                    return new
                    {
                        username = p.Username,
                        online = p.Online,
                        lastSeenUtc = p.LastSeenUtc,
                        label = p.Label,
                        preview = c?.Preview,
                        lastSentUtc = c?.LastSentUtc,
                        lastFromMe = c?.LastFromMe ?? false,
                        unreadCount = c?.UnreadCount ?? 0,
                    };
                })
                // Live conversations first, newest at the top; then everyone
                // never spoken to, with whoever is around right now above the
                // rest, because they are who you would actually message.
                .OrderByDescending(c => c.lastSentUtc.HasValue)
                .ThenByDescending(c => c.lastSentUtc)
                .ThenByDescending(c => c.online)
                .ThenBy(c => c.username, StringComparer.Ordinal)
                .ToList();

            return Results.Ok(contacts);
        });

        // One thread. Returns the newest ThreadLimit messages in chronological
        // order -- the client renders top to bottom and scrolls to the end.
        app.MapGet("/api/leagues/{leagueId}/messages/{peer}", async (
            string leagueId, string peer, string username, FantasyWarriorDbContext db) =>
        {
            var context = await ResolveAsync(db, leagueId, username, peer);
            if (context.Error is not null) return context.Error;
            var (league, me, other) = (context.League!, context.Me!, context.Peer!);

            var messages = await db.Messages.AsNoTracking()
                .Where(m => m.LeagueId == league.LeagueId
                            && ((m.SenderUserId == me.UserId && m.RecipientUserId == other.UserId)
                                || (m.SenderUserId == other.UserId && m.RecipientUserId == me.UserId)))
                .OrderByDescending(m => m.SentUtc).ThenByDescending(m => m.MessageId)
                .Take(ThreadLimit)
                .Select(m => new
                {
                    id = m.MessageId.ToString(),
                    fromMe = m.SenderUserId == me.UserId,
                    body = m.Body,
                    sentUtc = m.SentUtc,
                })
                .ToListAsync();

            messages.Reverse();
            return Results.Ok(messages);
        });

        app.MapPost("/api/leagues/{leagueId}/messages", async (
            string leagueId, SendMessageRequest req, FantasyWarriorDbContext db,
            IHubContext<LiveHub> hub) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.To))
                return Results.BadRequest(new { error = "Both a sender and a recipient are required." });

            var context = await ResolveAsync(db, leagueId, req.Username, req.To);
            if (context.Error is not null) return context.Error;
            var (league, me, other) = (context.League!, context.Me!, context.Peer!);

            var (body, error) = MessageRules.Prepare(req.Body);
            if (error is not null) return Results.BadRequest(new { error });

            var message = new Message
            {
                LeagueId = league.LeagueId,
                SenderUserId = me.UserId,
                RecipientUserId = other.UserId,
                Body = body,
                SentUtc = DateTime.UtcNow,
            };
            db.Messages.Add(message);
            await db.SaveChangesAsync();

            // The Nth message in this room, sender's side only — a milestone
            // count landing on a Fibonacci number earns the sender cockcoin.
            var sentCount = await db.Messages.CountAsync(m =>
                m.LeagueId == league.LeagueId && m.SenderUserId == me.UserId && m.RecipientUserId == other.UserId);
            if (FibonacciMilestones.RewardForCount(sentCount) is { } milestoneAmount)
            {
                db.CockcoinAwards.Add(new CockcoinAward
                {
                    UserId = me.UserId,
                    Amount = milestoneAmount,
                    Reason = CockcoinReasons.ChatMessageMilestone,
                    AwardedUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            // Persist first, then push. A message the recipient saw but that
            // never landed in the table would be worse than a slow one.
            var payload = new
            {
                id = message.MessageId.ToString(),
                leagueId = league.JoinCode,
                from = me.Username,
                to = other.Username,
                body = message.Body,
                sentUtc = message.SentUtc,
            };

            // The sender's own group too: he may have the app open elsewhere,
            // and that copy should not have to refetch to stay in step.
            await hub.Clients
                .Groups(LiveHub.UserGroup(other.UserId), LiveHub.UserGroup(me.UserId))
                .SendAsync("message", payload);

            return Results.Ok(payload);
        });

        // Opening a thread marks whatever it holds as read. Idempotent, and
        // scoped to inbound messages only -- the viewer's own are read by
        // definition.
        app.MapPost("/api/leagues/{leagueId}/messages/{peer}/read", async (
            string leagueId, string peer, ReadRequest req, FantasyWarriorDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { error = "A username is required." });

            var context = await ResolveAsync(db, leagueId, req.Username, peer);
            if (context.Error is not null) return context.Error;
            var (league, me, other) = (context.League!, context.Me!, context.Peer!);

            var marked = await db.Messages
                .Where(m => m.LeagueId == league.LeagueId
                            && m.RecipientUserId == me.UserId
                            && m.SenderUserId == other.UserId
                            && m.ReadUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadUtc, DateTime.UtcNow));

            return Results.Ok(new { marked });
        });

        // The topbar badge. Deliberately its own tiny route rather than a field
        // on the league response: it is refetched on every tab change, and it
        // has to stay cheap enough that nobody thinks twice about that.
        app.MapGet("/api/leagues/{leagueId}/unread", async (
            string leagueId, string username, FantasyWarriorDbContext db) =>
        {
            var context = await ResolveAsync(db, leagueId, username);
            if (context.Error is not null) return context.Error;

            var count = await db.Messages.CountAsync(m =>
                m.LeagueId == context.League!.LeagueId
                && m.RecipientUserId == context.Me!.UserId
                && m.ReadUtc == null);

            return Results.Ok(new { count });
        });
    }

    /// <summary>
    /// Resolves the league, the caller and optionally the other party, and
    /// checks that everyone involved actually belongs to the league. Every
    /// route needs the same three answers, and getting one of them wrong is how
    /// a private thread leaks.
    /// </summary>
    private static async Task<MessageContext> ResolveAsync(
        FantasyWarriorDbContext db, string leagueCode, string username, string? peerUsername = null)
    {
        var league = await Queries.LeagueByCodeAsync(db, leagueCode);
        if (league is null)
            return new MessageContext(Error: Results.NotFound(new { error = "League not found." }));

        var me = await FindMemberAsync(db, league.LeagueId, username);
        if (me is null)
            return new MessageContext(Error: Results.Json(
                new { error = "You are not a member of this league." }, statusCode: 403));

        if (peerUsername is null) return new MessageContext(league, me);

        var peer = await FindMemberAsync(db, league.LeagueId, peerUsername);
        if (peer is null)
            return new MessageContext(Error: Results.NotFound(
                new { error = "That GM is not in this league." }));

        if (peer.UserId == me.UserId)
            return new MessageContext(Error: Results.BadRequest(
                new { error = "You cannot message yourself." }));

        return new MessageContext(league, me, peer);
    }

    private static Task<User?> FindMemberAsync(FantasyWarriorDbContext db, int leagueId, string username)
    {
        var normalized = Queries.Normalize(username);
        return db.LeagueMembers
            .Where(m => m.LeagueId == leagueId && m.User!.Username == normalized)
            .Select(m => m.User)
            .FirstOrDefaultAsync();
    }

    private sealed record MessageContext(
        League? League = null, User? Me = null, User? Peer = null, IResult? Error = null);
}

public record SendMessageRequest(string? Username, string? To, string? Body);

public record ReadRequest(string? Username);
