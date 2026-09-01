using FantasyWarrior.Core.Cockcoin;
using FantasyWarrior.Core.Cockman;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

public record AnswerCampaignRequest(string? ChoiceKey);

/// <summary>
/// Cockman campaigns — scheduled message/question/reward notifications shown
/// once per user. See .claude/doc/cockman-concept.md. Not league-scoped: a
/// campaign is a fact about the user, not about any one pool.
/// </summary>
public static class CockmanCampaignEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/users/{username}/cockman/campaign", async (string username, FantasyWarriorDbContext db) =>
        {
            var user = await FindUserAsync(db, username);
            if (user is null) return Results.NotFound(new { error = "User not found." });

            var seen = await db.CockmanCampaignViews
                .Where(v => v.UserId == user.UserId)
                .Select(v => v.CockmanCampaignId)
                .ToListAsync();
            var candidates = await db.CockmanCampaigns
                .Select(c => new CampaignCandidate(c.CockmanCampaignId, c.StartUtc, c.EndUtc))
                .ToListAsync();

            var nextId = CampaignSelection.SelectNext(candidates, seen, DateTime.UtcNow);
            if (nextId is null) return Results.Ok((object?)null);

            var campaign = await db.CockmanCampaigns.FirstAsync(c => c.CockmanCampaignId == nextId);
            return Results.Ok(new
            {
                id = campaign.CockmanCampaignId.ToString(),
                key = campaign.Key,
                hasQuestion = campaign.HasQuestion,
                choiceKeys = campaign.ChoiceKeys,
                rewardAmount = campaign.RewardAmount,
            });
        });

        app.MapPost("/api/users/{username}/cockman/campaign/{campaignId:int}/dismiss", async (
            string username, int campaignId, FantasyWarriorDbContext db) =>
        {
            var user = await FindUserAsync(db, username);
            if (user is null) return Results.NotFound(new { error = "User not found." });

            // Idempotent: a dismiss must never clobber an existing answer/reward.
            var existing = await db.CockmanCampaignViews
                .FirstOrDefaultAsync(v => v.CockmanCampaignId == campaignId && v.UserId == user.UserId);
            if (existing is null)
            {
                db.CockmanCampaignViews.Add(new CockmanCampaignView
                {
                    CockmanCampaignId = campaignId, UserId = user.UserId, ViewedUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/users/{username}/cockman/campaign/{campaignId:int}/answer", async (
            string username, int campaignId, AnswerCampaignRequest req, FantasyWarriorDbContext db) =>
        {
            var user = await FindUserAsync(db, username);
            if (user is null) return Results.NotFound(new { error = "User not found." });

            var campaign = await db.CockmanCampaigns.FirstOrDefaultAsync(c => c.CockmanCampaignId == campaignId);
            if (campaign is null) return Results.NotFound(new { error = "Campaign not found." });
            if (!campaign.HasQuestion || campaign.ChoiceKeys is null || req.ChoiceKey is null
                || !campaign.ChoiceKeys.Contains(req.ChoiceKey))
                return Results.BadRequest(new { error = "Invalid choice for this campaign." });

            // Idempotent, same as dismiss — never reward twice for one campaign.
            var existing = await db.CockmanCampaignViews
                .FirstOrDefaultAsync(v => v.CockmanCampaignId == campaignId && v.UserId == user.UserId);
            if (existing is not null)
                return Results.Ok(new { ok = true, cockcoinAwarded = 0, cockcoinBalance = await BalanceAsync(db, user.UserId) });

            var view = new CockmanCampaignView
            {
                CockmanCampaignId = campaignId,
                UserId = user.UserId,
                ViewedUtc = DateTime.UtcNow,
                ChosenAnswer = req.ChoiceKey,
            };

            var awarded = 0;
            if (campaign.RewardAmount is { } amount && amount > 0)
            {
                view.RewardedUtc = DateTime.UtcNow;
                db.CockcoinAwards.Add(new CockcoinAward
                {
                    UserId = user.UserId,
                    Amount = amount,
                    Reason = CockcoinReasons.CockmanCampaign,
                    AwardedUtc = DateTime.UtcNow,
                });
                awarded = amount;
            }

            db.CockmanCampaignViews.Add(view);
            await db.SaveChangesAsync();

            return Results.Ok(new { ok = true, cockcoinAwarded = awarded, cockcoinBalance = await BalanceAsync(db, user.UserId) });
        });
    }

    private static Task<User?> FindUserAsync(FantasyWarriorDbContext db, string username)
    {
        var normalized = Queries.Normalize(username);
        return db.Users.FirstOrDefaultAsync(u => u.Username == normalized);
    }

    private static async Task<int> BalanceAsync(FantasyWarriorDbContext db, int userId) =>
        await db.CockcoinBalances.Where(b => b.UserId == userId).Select(b => (int?)b.Balance).FirstOrDefaultAsync() ?? 0;
}
