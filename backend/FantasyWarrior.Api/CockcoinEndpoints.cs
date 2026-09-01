using FantasyWarrior.Core.Cockcoin;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>
/// Cockcoin — Fantasy Warrior's in-universe fake token economy. See
/// .claude/doc/cockman-concept.md. The read/ack surface for the balance and
/// the done-deal bonus's pending-reward pop; awarding itself happens inline
/// wherever an earning action is handled (e.g. TradeEndpoints' vote route,
/// WeekAheadJob's nightly landing loop), never here.
/// </summary>
public static class CockcoinEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/users/{username}/cockcoin", async (string username, FantasyWarriorDbContext db) =>
        {
            var user = await FindUserAsync(db, username);
            if (user is null) return Results.NotFound(new { error = "User not found." });

            // No row at all for a user who has never earned any — everyone
            // starts at 0, not "unknown".
            var balance = await db.CockcoinBalances
                .Where(b => b.UserId == user.UserId)
                .Select(b => (int?)b.Balance)
                .FirstOrDefaultAsync() ?? 0;

            return Results.Ok(new { balance });
        });

        // The done-deal bonus is awarded overnight, with nobody watching —
        // this is what turns it into a moment the next time the GM opens the
        // app instead of a number that quietly changed. Summed rather than
        // shown one award at a time: several trades landing the same night
        // carry no per-item content worth separating, so one "+20 CK" beats
        // a queue of near-identical pops.
        app.MapGet("/api/users/{username}/cockcoin/pending-reward", async (string username, FantasyWarriorDbContext db) =>
        {
            var user = await FindUserAsync(db, username);
            if (user is null) return Results.NotFound(new { error = "User not found." });

            var pending = await db.CockcoinAwards
                .Where(a => a.UserId == user.UserId && a.Reason == CockcoinReasons.DoneDeal && a.AcknowledgedUtc == null)
                .SumAsync(a => (int?)a.Amount) ?? 0;

            return pending > 0 ? Results.Ok(new { amount = pending }) : Results.Ok((object?)null);
        });

        app.MapPost("/api/users/{username}/cockcoin/pending-reward/ack", async (string username, FantasyWarriorDbContext db) =>
        {
            var user = await FindUserAsync(db, username);
            if (user is null) return Results.NotFound(new { error = "User not found." });

            // Marks every currently-unacknowledged done-deal award, matching
            // the sum the GM was just shown. A trade landing between the GET
            // above and this ack sweeps in unshown — an accepted race, same
            // class as the Cockman campaign endpoints already carry.
            var acked = await db.CockcoinAwards
                .Where(a => a.UserId == user.UserId && a.Reason == CockcoinReasons.DoneDeal && a.AcknowledgedUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.AcknowledgedUtc, DateTime.UtcNow));

            return Results.Ok(new { ok = true, acked });
        });
    }

    private static Task<User?> FindUserAsync(FantasyWarriorDbContext db, string username)
    {
        var normalized = Queries.Normalize(username);
        return db.Users.FirstOrDefaultAsync(u => u.Username == normalized);
    }
}
