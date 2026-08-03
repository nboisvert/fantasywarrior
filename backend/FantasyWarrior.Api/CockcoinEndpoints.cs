using FantasyWarrior.Data;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>
/// Cockcoin — Fantasy Warrior's in-universe fake token economy. See
/// .claude/doc/cockman-concept.md. This is the one read endpoint; awarding
/// happens inline wherever an earning action is handled (e.g. TradeEndpoints'
/// vote route), never here.
/// </summary>
public static class CockcoinEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/users/{username}/cockcoin", async (string username, FantasyWarriorDbContext db) =>
        {
            var normalized = Queries.Normalize(username);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == normalized);
            if (user is null) return Results.NotFound(new { error = "User not found." });

            // No row at all for a user who has never earned any — everyone
            // starts at 0, not "unknown".
            var balance = await db.CockcoinBalances
                .Where(b => b.UserId == user.UserId)
                .Select(b => (int?)b.Balance)
                .FirstOrDefaultAsync() ?? 0;

            return Results.Ok(new { balance });
        });
    }
}
