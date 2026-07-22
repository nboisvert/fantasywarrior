using FantasyWarrior.Api;
using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Players;
using FantasyWarrior.Core.Users;
using Google.Cloud.Firestore;

// TEMPORARY AUTH MODEL: the API trusts the username sent by the client.
// Firebase Auth token verification replaces this when real auth lands.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton<FirestoreDb>(_ =>
    FirestoreDb.Create(Environment.GetEnvironmentVariable("FIRESTORE_PROJECT_ID") ?? "fantasywarriordb"));
builder.Services.AddSingleton<PlayerCache>();

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => "Fantasy Warrior API");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

static string Normalize(string username) => username.Trim().ToLowerInvariant();

app.MapPost("/api/login", async (LoginRequest req, FirestoreDb db) =>
{
    var display = req.Username?.Trim() ?? "";
    if (display.Length is < 2 or > 30)
        return Results.BadRequest(new { error = "Username must be 2-30 characters." });

    var id = Normalize(display);
    var doc = db.Collection("users").Document(id);
    var now = Timestamp.GetCurrentTimestamp();
    var snapshot = await doc.GetSnapshotAsync();
    if (snapshot.Exists)
        await doc.UpdateAsync("lastLoginUtc", now);
    else
        await doc.SetAsync(new User { DisplayName = display, CreatedUtc = now, LastLoginUtc = now });
    return Results.Ok(new { username = id, displayName = snapshot.Exists ? snapshot.GetValue<string>("displayName") : display });
});

app.MapGet("/api/users/{username}/leagues", async (string username, FirestoreDb db) =>
{
    var snapshot = await db.Collection("leagues")
        .WhereArrayContains("memberUsernames", Normalize(username))
        .GetSnapshotAsync();
    return Results.Ok(snapshot.Documents.Select(d =>
    {
        var league = d.ConvertTo<League>();
        return new { id = d.Id, league.Name, league.Season, league.CapAmount, members = league.MemberUsernames.Count };
    }));
});

app.MapPost("/api/leagues", async (CreateLeagueRequest req, FirestoreDb db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Username))
        return Results.BadRequest(new { error = "Name and username are required." });

    var username = Normalize(req.Username);
    var now = Timestamp.GetCurrentTimestamp();
    var leagueDoc = db.Collection("leagues").Document();
    await leagueDoc.SetAsync(new League
    {
        Name = req.Name.Trim(),
        Season = req.Season ?? "20262027",
        CommissionerUsername = username,
        MemberUsernames = [username],
        CapAmount = req.CapAmount,
        CreatedUtc = now,
    });
    await leagueDoc.Collection("teams").Document(username).SetAsync(new Team
    {
        Name = string.IsNullOrWhiteSpace(req.TeamName) ? $"Team {req.Username.Trim()}" : req.TeamName.Trim(),
        OwnerUsername = username,
        CreatedUtc = now,
    });
    return Results.Ok(new { id = leagueDoc.Id });
});

app.MapPost("/api/leagues/{leagueId}/join", async (string leagueId, JoinLeagueRequest req, FirestoreDb db) =>
{
    if (string.IsNullOrWhiteSpace(req.Username))
        return Results.BadRequest(new { error = "Username is required." });

    var leagueDoc = db.Collection("leagues").Document(leagueId);
    if (!(await leagueDoc.GetSnapshotAsync()).Exists)
        return Results.NotFound(new { error = "League not found." });

    var username = Normalize(req.Username);
    await leagueDoc.UpdateAsync("memberUsernames", FieldValue.ArrayUnion(username));
    var teamDoc = leagueDoc.Collection("teams").Document(username);
    if (!(await teamDoc.GetSnapshotAsync()).Exists)
        await teamDoc.SetAsync(new Team
        {
            Name = string.IsNullOrWhiteSpace(req.TeamName) ? $"Team {req.Username.Trim()}" : req.TeamName.Trim(),
            OwnerUsername = username,
            CreatedUtc = Timestamp.GetCurrentTimestamp(),
        });
    return Results.Ok(new { id = leagueId });
});

app.MapGet("/api/leagues/{leagueId}", async (string leagueId, FirestoreDb db, PlayerCache players) =>
{
    var leagueSnap = await db.Collection("leagues").Document(leagueId).GetSnapshotAsync();
    if (!leagueSnap.Exists)
        return Results.NotFound(new { error = "League not found." });

    var league = leagueSnap.ConvertTo<League>();
    var teamsSnap = await leagueSnap.Reference.Collection("teams").GetSnapshotAsync();
    var teams = teamsSnap.Documents.Select(d => d.ConvertTo<Team>()).ToList();
    var playersById = await players.GetByIdsAsync(teams.SelectMany(t => t.PlayerIds));

    return Results.Ok(new
    {
        id = leagueSnap.Id,
        league.Name,
        league.Season,
        league.CapAmount,
        league.CommissionerUsername,
        members = league.MemberUsernames,
        teams = teams.Select(t => new
        {
            t.Name,
            t.OwnerUsername,
            capTotal = t.PlayerIds.Sum(id => playersById.GetValueOrDefault(id)?.CapHit ?? 0),
            players = t.PlayerIds
                .Select(id => playersById.GetValueOrDefault(id))
                .Where(p => p is not null)
                .Select(p => PlayerDto.From(p!)),
        }),
    });
});

app.MapPost("/api/leagues/{leagueId}/teams/{username}/roster", async (
    string leagueId, string username, RosterChangeRequest req, FirestoreDb db, PlayerCache players) =>
{
    var teamDoc = db.Collection("leagues").Document(leagueId).Collection("teams").Document(Normalize(username));
    if (!(await teamDoc.GetSnapshotAsync()).Exists)
        return Results.NotFound(new { error = "Team not found." });

    var player = (await players.GetByIdsAsync([req.PlayerId])).GetValueOrDefault(req.PlayerId);
    if (player is null)
        return Results.BadRequest(new { error = "Unknown player id." });

    // One owner per player per league.
    var teamsSnap = await db.Collection("leagues").Document(leagueId).Collection("teams").GetSnapshotAsync();
    var taken = teamsSnap.Documents
        .FirstOrDefault(d => d.ConvertTo<Team>().PlayerIds.Contains(req.PlayerId));
    if (taken is not null && taken.Id != Normalize(username))
        return Results.Conflict(new { error = $"{player.FirstName} {player.LastName} is already on team '{taken.GetValue<string>("name")}'." });

    await teamDoc.UpdateAsync("playerIds", FieldValue.ArrayUnion(req.PlayerId));
    return Results.Ok(PlayerDto.From(player));
});

app.MapDelete("/api/leagues/{leagueId}/teams/{username}/roster/{playerId:long}", async (
    string leagueId, string username, long playerId, FirestoreDb db) =>
{
    var teamDoc = db.Collection("leagues").Document(leagueId).Collection("teams").Document(Normalize(username));
    if (!(await teamDoc.GetSnapshotAsync()).Exists)
        return Results.NotFound(new { error = "Team not found." });
    await teamDoc.UpdateAsync("playerIds", FieldValue.ArrayRemove(playerId));
    return Results.Ok();
});

app.MapGet("/api/players", async (string? q, PlayerCache players) =>
{
    var results = await players.SearchAsync(q ?? "", limit: 20);
    return Results.Ok(results.Select(PlayerDto.From));
});

app.Run();

record LoginRequest(string? Username);
record CreateLeagueRequest(string? Name, string? Username, string? TeamName, string? Season, long? CapAmount);
record JoinLeagueRequest(string? Username, string? TeamName);
record RosterChangeRequest(long PlayerId);

record PlayerDto(long Id, string Name, string Position, string Team, string Status, long? CapHit, string? HeadshotUrl)
{
    public static PlayerDto From(Player p) =>
        new(p.NhlId, $"{p.FirstName} {p.LastName}", p.Position, p.TeamAbbrev, p.Status, p.CapHit, p.HeadshotUrl);
}
