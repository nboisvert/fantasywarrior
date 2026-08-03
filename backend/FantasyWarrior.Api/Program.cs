using FantasyWarrior.Api;
using FantasyWarrior.Core.Messaging;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using Microsoft.EntityFrameworkCore;

// TEMPORARY AUTH MODEL: the API trusts the username sent by the client.
// Token verification replaces this when real auth lands. With weekly lineups
// this is materially worse than it sounds -- silently benching a rival's best
// player every Sunday would be undetectable.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddFantasyWarriorData();

// Resolves the simulation cursor when a season replay is running, the real clock
// otherwise. Scoped alongside the DbContext it reads from, with its own short
// TTL so this costs at most one query every few seconds rather than one per
// request.
builder.Services.AddScoped<SimulationClockService>();

builder.Services.AddSignalR();

// Singletons, and both are process memory by design. PresenceService is now the
// *only* record of who is online, which is what makes --max-replicas 1 in
// api-deploy.yml structural rather than a preference. LastSeenStamper holds the
// throttle that keeps presence from being a write on every request.
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<LastSeenStamper>();

var app = builder.Build();
app.UseCors();

// Explicit, so the presence middleware below is unambiguously downstream of
// routing and can read the {username} route value.
app.UseRouting();

// Presence costs no heartbeat: ordinary traffic is the signal. See
// PresenceMiddleware for why the request body is not consulted.
app.UsePresenceStamping();

app.MapGet("/", () => "Fantasy Warrior API");

// Deliberately does not touch the database: this has to answer even when the
// connection string is wrong, or a misconfigured deploy presents as an ingress
// that completes the TLS handshake and then hangs forever, which says nothing
// about its own cause.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Whether the database is actually reachable, and how long it took — the
// serverless tier's resume shows up here as several seconds on the first call
// after an idle hour. Separate from /health on purpose: a container that is up
// but cannot reach its database is a different problem from one that is down.
app.MapGet("/health/db", async (FantasyWarriorDbContext db) =>
{
    var started = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        var players = await db.Players.CountAsync();
        return Results.Ok(new { status = "healthy", players, elapsedMs = started.ElapsedMilliseconds });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { status = "unreachable", error = ex.Message, elapsedMs = started.ElapsedMilliseconds },
            statusCode: 503);
    }
});

// Whether a season replay is in progress and what day it thinks it is. The one
// place to look when the app's idea of "this week" seems wrong.
app.MapGet("/api/clock", async (SimulationClockService clock) =>
{
    var state = await clock.StateAsync();
    var now = await clock.NowAsync();
    return Results.Ok(new
    {
        simulated = state is not null,
        asOfDate = state?.AsOfDate,
        season = state?.Season,
        todayEt = PoolClock.TodayEt(now),
        lastStatDate = PoolClock.LastStatDate(now),
    });
});

LeagueEndpoints.Map(app);
LineupEndpoints.Map(app);
TradeEndpoints.Map(app);
PlayerEndpoints.Map(app);
CockcoinEndpoints.Map(app);
MessageEndpoints.Map(app);

// The one live channel: direct messages and presence today, event pop-ups next.
// Push only -- clients never call into it, they POST and it pushes.
app.MapHub<LiveHub>("/hubs/live");

app.Run();
