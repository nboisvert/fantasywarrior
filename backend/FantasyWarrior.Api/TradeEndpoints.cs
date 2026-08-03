using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

public static class TradeEndpoints
{
    /// <summary>
    /// The wire spelling of a status. The column is a tinyint enum now; the
    /// frontend has always read these strings.
    /// </summary>
    private static string Wire(TradeStatus s) => s switch
    {
        TradeStatus.Pending => "pending",
        TradeStatus.Declined => "declined",
        TradeStatus.Cancelled => "cancelled",
        TradeStatus.Accepted => "accepted",
        _ => "processed",
    };

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/leagues/{leagueId}/trades", async (
            string leagueId, ProposeTradeRequest req, FantasyWarriorDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.CounterpartyUsername))
                return Results.BadRequest(new { error = "Username and counterpartyUsername are required." });

            var proposer = Queries.Normalize(req.Username);
            var counterparty = Queries.Normalize(req.CounterpartyUsername);
            if (proposer == counterparty)
                return Results.BadRequest(new { error = "Cannot trade with yourself." });

            var fromProposer = req.PlayersFromProposer ?? [];
            var fromCounterparty = req.PlayersFromCounterparty ?? [];
            if (fromProposer.Count == 0 && fromCounterparty.Count == 0)
                return Results.BadRequest(new { error = "Trade must include at least one player." });
            if (fromProposer.Intersect(fromCounterparty).Any())
                return Results.BadRequest(new { error = "A player can't appear on both sides of a trade." });

            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var proposerTeam = await Queries.TeamAsync(db, league.LeagueId, proposer);
            var counterpartyTeam = await Queries.TeamAsync(db, league.LeagueId, counterparty);
            if (proposerTeam is null || counterpartyTeam is null)
                return Results.NotFound(new { error = "Team not found." });

            // Ownership is checked against open roster spots, which is the only
            // record of who holds whom — there is no denormalised player list to
            // fall out of step with it any more.
            var held = await db.RosterSpots
                .Where(s => s.LeagueId == league.LeagueId && s.EndDate == null)
                .Select(s => new { s.TeamId, s.PlayerId })
                .ToListAsync();
            var proposerHas = held.Where(h => h.TeamId == proposerTeam.TeamId).Select(h => h.PlayerId).ToHashSet();
            var counterpartyHas = held.Where(h => h.TeamId == counterpartyTeam.TeamId).Select(h => h.PlayerId).ToHashSet();

            if (fromProposer.Any(id => !proposerHas.Contains(id)))
                return Results.BadRequest(new { error = "You can only offer players on your own roster." });
            if (fromCounterparty.Any(id => !counterpartyHas.Contains(id)))
                return Results.BadRequest(new { error = "Requested players aren't on that team's roster." });

            var trade = new Trade
            {
                LeagueId = league.LeagueId,
                ProposerTeamId = proposerTeam.TeamId,
                CounterpartyTeamId = counterpartyTeam.TeamId,
                Status = TradeStatus.Pending,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Trades.Add(trade);
            await db.SaveChangesAsync();

            foreach (var playerId in fromProposer)
                db.TradeAssets.Add(new TradeAsset
                {
                    TradeId = trade.TradeId, FromTeamId = proposerTeam.TeamId, ToTeamId = counterpartyTeam.TeamId,
                    AssetType = TradeAssetType.Player, PlayerId = playerId,
                });
            foreach (var playerId in fromCounterparty)
                db.TradeAssets.Add(new TradeAsset
                {
                    TradeId = trade.TradeId, FromTeamId = counterpartyTeam.TeamId, ToTeamId = proposerTeam.TeamId,
                    AssetType = TradeAssetType.Player, PlayerId = playerId,
                });
            await db.SaveChangesAsync();

            return Results.Ok(new { id = trade.TradeId.ToString() });
        });

        app.MapPost("/api/leagues/{leagueId}/trades/{tradeId}/respond", async (
            string leagueId, string tradeId, RespondTradeRequest req, FantasyWarriorDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { error = "Username is required." });
            if (!int.TryParse(tradeId, out var id))
                return Results.NotFound(new { error = "Trade not found." });

            var trade = await db.Trades
                .Include(t => t.ProposerTeam).ThenInclude(t => t!.Owner)
                .Include(t => t.CounterpartyTeam).ThenInclude(t => t!.Owner)
                .FirstOrDefaultAsync(t => t.TradeId == id);
            if (trade is null) return Results.NotFound(new { error = "Trade not found." });

            var username = Queries.Normalize(req.Username);
            var isProposer = trade.ProposerTeam!.Owner!.Username == username;
            var isCounterparty = trade.CounterpartyTeam!.Owner!.Username == username;
            var now = DateTime.UtcNow;

            if (req.Accept)
            {
                if (trade.Status != TradeStatus.Pending || !isCounterparty)
                    return Results.Json(new { error = "Only the receiving team can accept this trade." }, statusCode: 403);
                trade.Status = TradeStatus.Accepted;
                trade.RespondedUtc = now;
                await db.SaveChangesAsync();
                return Results.Ok(new
                {
                    ok = true, status = "accepted",
                    note = "Processed at the next week boundary.",
                });
            }

            // Declining branches by who is acting: the proposer withdrawing his
            // own offer is "cancelled", the counterparty rejecting it is
            // "declined" — distinct statuses so the status alone says who acted.
            if (isProposer)
            {
                if (trade.Status != TradeStatus.Pending)
                    return Results.Json(new { error = "This offer can no longer be cancelled." }, statusCode: 403);
                trade.Status = TradeStatus.Cancelled;
                trade.RespondedUtc = now;
                await db.SaveChangesAsync();
                return Results.Ok(new { ok = true, status = "cancelled" });
            }

            if (trade.Status != TradeStatus.Pending || !isCounterparty)
                return Results.Json(new { error = "Only the receiving team can decline this trade." }, statusCode: 403);
            trade.Status = TradeStatus.Declined;
            trade.RespondedUtc = now;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, status = "declined" });
        });

        app.MapGet("/api/leagues/{leagueId}/trades", async (
            string leagueId, string? username, FantasyWarriorDbContext db) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });
            if (string.IsNullOrWhiteSpace(username))
                return Results.BadRequest(new { error = "username is required." });
            var viewer = Queries.Normalize(username);

            var trades = await db.Trades
                .Where(t => t.LeagueId == league.LeagueId)
                .Include(t => t.Assets)
                .Include(t => t.ProposerTeam).ThenInclude(t => t!.Owner)
                .Include(t => t.CounterpartyTeam).ThenInclude(t => t!.Owner)
                .Include(t => t.Votes).ThenInclude(v => v.User)
                .OrderByDescending(t => t.CreatedUtc)
                .ToListAsync();

            // pending/declined/cancelled are private to the two parties;
            // accepted/processed are public to the whole league.
            trades = trades.Where(t =>
                t.Status is TradeStatus.Accepted or TradeStatus.Processed
                || t.ProposerTeam!.Owner!.Username == viewer
                || t.CounterpartyTeam!.Owner!.Username == viewer).ToList();

            var allPlayerIds = trades.SelectMany(t => t.Assets)
                .Where(a => a.PlayerId is not null).Select(a => a.PlayerId!.Value).Distinct().ToList();
            var players = await db.Players.Where(p => allPlayerIds.Contains(p.PlayerId))
                .ToDictionaryAsync(p => p.PlayerId);

            object[] Side(Trade t, int fromTeamId) =>
            [
                .. t.Assets
                    .Where(a => a.FromTeamId == fromTeamId && a.AssetType == TradeAssetType.Player)
                    .Select(a =>
                    {
                        players.TryGetValue(a.PlayerId!.Value, out var p);
                        return (object)new
                        {
                            id = a.PlayerId!.Value,
                            name = p?.FullName ?? "Unknown player",
                            position = p?.Position,
                        };
                    })
            ];

            return Results.Ok(trades.Select(t =>
            {
                var votes = t.Status == TradeStatus.Processed ? t.Votes.ToList() : [];
                var mine = votes.FirstOrDefault(v => v.User!.Username == viewer);
                return new
                {
                    id = t.TradeId.ToString(),
                    proposerUsername = t.ProposerTeam!.Owner!.Username,
                    proposerTeamName = t.ProposerTeam.Name,
                    counterpartyUsername = t.CounterpartyTeam!.Owner!.Username,
                    counterpartyTeamName = t.CounterpartyTeam.Name,
                    playersFromProposer = Side(t, t.ProposerTeamId),
                    playersFromCounterparty = Side(t, t.CounterpartyTeamId),
                    status = Wire(t.Status),
                    createdUtc = t.CreatedUtc,
                    respondedUtc = t.RespondedUtc,
                    processedUtc = t.ProcessedUtc,
                    // Withheld until the viewer has voted themselves — not just
                    // hidden in the UI, since the point is nobody's opinion gets
                    // to lean on the crowd's before they've formed their own
                    // (2026-08-02, per Nick).
                    votes = mine is null ? null : new
                    {
                        proposer = votes.Count(v => v.FavoredTeamId == t.ProposerTeamId),
                        fair = votes.Count(v => v.FavoredTeamId is null),
                        counterparty = votes.Count(v => v.FavoredTeamId == t.CounterpartyTeamId),
                        total = votes.Count,
                    },
                    myVote = mine is null ? null : new
                    {
                        favoredUsername = mine.FavoredTeamId == t.ProposerTeamId ? t.ProposerTeam.Owner.Username
                            : mine.FavoredTeamId == t.CounterpartyTeamId ? t.CounterpartyTeam.Owner.Username
                            : null,
                    },
                };
            }));
        });

        app.MapPost("/api/leagues/{leagueId}/trades/{tradeId}/vote", async (
            string leagueId, string tradeId, VoteTradeRequest req, FantasyWarriorDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { error = "Username is required." });
            if (!int.TryParse(tradeId, out var id))
                return Results.NotFound(new { error = "Trade not found." });

            var trade = await db.Trades
                .Include(t => t.ProposerTeam).ThenInclude(t => t!.Owner)
                .Include(t => t.CounterpartyTeam).ThenInclude(t => t!.Owner)
                .FirstOrDefaultAsync(t => t.TradeId == id);
            if (trade is null) return Results.NotFound(new { error = "Trade not found." });
            if (trade.Status != TradeStatus.Processed)
                return Results.BadRequest(new { error = "Only processed trades can be rated." });

            // The frontend still speaks in usernames; the column is a team id,
            // which is what lets a future "who wins their trades" rollup filter
            // across every trade without knowing who proposed which.
            int? favoredTeamId =
                req.FavoredUsername is null ? null
                : Queries.Normalize(req.FavoredUsername) == trade.ProposerTeam!.Owner!.Username ? trade.ProposerTeamId
                : Queries.Normalize(req.FavoredUsername) == trade.CounterpartyTeam!.Owner!.Username ? trade.CounterpartyTeamId
                : -1;

            if (favoredTeamId == -1)
                return Results.BadRequest(new
                {
                    error = "Invalid vote: favoredUsername must be null (fair) or one of the two teams.",
                });

            var username = Queries.Normalize(req.Username);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null) return Results.NotFound(new { error = "User not found." });

            var vote = await db.TradeVotes.FirstOrDefaultAsync(v => v.TradeId == id && v.UserId == user.UserId);
            if (vote is null)
                db.TradeVotes.Add(new TradeVote
                {
                    TradeId = id, UserId = user.UserId, FavoredTeamId = favoredTeamId, VotedUtc = DateTime.UtcNow,
                });
            else
            {
                vote.FavoredTeamId = favoredTeamId;
                vote.VotedUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
    }
}

public record ProposeTradeRequest(
    string? Username, string? CounterpartyUsername,
    List<long>? PlayersFromProposer, List<long>? PlayersFromCounterparty);
public record RespondTradeRequest(string? Username, bool Accept);
public record VoteTradeRequest(string? Username, string? FavoredUsername);
