using FantasyWarrior.Data;
using FantasyWarrior.Jobs.Sql;

namespace FantasyWarrior.Api;

/// <summary>
/// Lets the season replay be advanced from a phone instead of a PowerShell
/// prompt on Nick's PC -- see .claude/doc/testmode.md and the /testmode skill,
/// which still describe the CLI job this wraps.
///
/// Gated on <c>username == "nick"</c> rather than left open like every other
/// endpoint: <see cref="SimAdvanceJob"/> banks weeks permanently and executes
/// trades for *both* leagues at once, so a stray tap from a pool-mate's phone
/// is a different order of mistake than a bad lineup edit. This is not real
/// auth -- the app has none yet -- just a guard against an accidental tap
/// until the app trusts something stronger than the username the client sends.
/// Meant to go away with the rest of test mode once the real season starts.
/// </summary>
public static class TestModeEndpoints
{
    // SimAdvanceJob and everything it calls (NightlyJob, PeriodRollupJob, ...)
    // report through Console.WriteLine, same as every other console job -- there
    // is no ILogger to hook instead. Console.Out/Error are process-global, so
    // this serializes advances one at a time and points the console at a buffer
    // only for the duration of a run, rather than trusting that no concurrent
    // request logs anything while it holds the redirect.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/testmode/advance", async (
            string username, string to, bool? dryRun, FantasyWarriorDbContext db) =>
        {
            if (Queries.Normalize(username) != "nick")
                return Results.Json(new { error = "Test mode is Nick-only." }, statusCode: 403);

            if (!DateOnly.TryParse(to, out var target))
                return Results.BadRequest(new { error = $"\"{to}\" is not a date (expected YYYY-MM-DD)." });

            if (!await Gate.WaitAsync(TimeSpan.Zero))
                return Results.Json(new { error = "Another advance is already running." }, statusCode: 409);

            var originalOut = Console.Out;
            var originalError = Console.Error;
            var captured = new StringWriter();
            try
            {
                Console.SetOut(captured);
                Console.SetError(captured);
                var exitCode = await new SimAdvanceJob(db).RunAsync(target, dryRun ?? false);
                return Results.Ok(new { ok = exitCode == 0, output = captured.ToString() });
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                Gate.Release();
            }
        });
    }
}
