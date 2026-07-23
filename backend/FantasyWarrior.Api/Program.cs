using FantasyWarrior.Api;
using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Players;
using FantasyWarrior.Core.Scoring;
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

// NHL "game day" runs on Eastern Time.
static string EtToday()
{
    TimeZoneInfo tz;
    try { tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
    catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).ToString("yyyy-MM-dd");
}

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
        RuleConfig = new RuleConfig(),
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

app.MapMethods("/api/leagues/{leagueId}/rules", ["PATCH"], async (string leagueId, UpdateRulesRequest req, FirestoreDb db) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || req.RuleConfig is null)
        return Results.BadRequest(new { error = "Username and ruleConfig are required." });

    var leagueDoc = db.Collection("leagues").Document(leagueId);
    var leagueSnap = await leagueDoc.GetSnapshotAsync();
    if (!leagueSnap.Exists)
        return Results.NotFound(new { error = "League not found." });
    if (leagueSnap.ConvertTo<League>().CommissionerUsername != Normalize(req.Username))
        return Results.Json(new { error = "Only the commissioner can change the rules." }, statusCode: 403);

    await leagueDoc.UpdateAsync("ruleConfig", req.RuleConfig);
    return Results.Ok(new { ok = true, note = "Scores refresh at the next nightly calculation (or run score-calc)." });
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
    var totalsById = await PlayerTotalsSource.FetchWithCacheAsync(db, teams.SelectMany(t => t.PlayerIds).ToList(), league.Season);

    return Results.Ok(new
    {
        id = leagueSnap.Id,
        league.Name,
        league.Season,
        league.CapAmount,
        league.CommissionerUsername,
        league.RuleConfig,
        members = league.MemberUsernames,
        teams = teams
            .OrderByDescending(t => t.Score)
            .ThenBy(t => t.Name)
            .Select(t => new
            {
                t.Name,
                t.OwnerUsername,
                t.Score,
                t.RawTopXScore,
                t.AdjustmentsTotal,
                ptsPerGame = PtsPerGame(t, totalsById),
                capTotal = t.PlayerIds.Sum(id => playersById.GetValueOrDefault(id)?.CapHit ?? 0),
                players = t.PlayerIds
                    .Select(id => playersById.GetValueOrDefault(id))
                    .Where(p => p is not null)
                    .Select(p => new
                    {
                        Dto = PlayerDto.From(p!),
                        Points = t.PlayerPoints.GetValueOrDefault(p!.NhlId.ToString(), 0),
                        Counted = t.CountedPlayerIds.Contains(p.NhlId),
                        NhlPoints = (totalsById.GetValueOrDefault(p.NhlId)?.Goals ?? 0)
                            + (totalsById.GetValueOrDefault(p.NhlId)?.Assists ?? 0),
                    })
                    .OrderByDescending(x => x.Points)
                    .Select(x => new
                    {
                        x.Dto.Id, x.Dto.Name, x.Dto.Position, x.Dto.Team, x.Dto.Status,
                        x.Dto.CapHit, x.Dto.HeadshotUrl, x.Points, x.Counted, x.NhlPoints,
                    }),
            }),
    });
});

app.MapPost("/api/leagues/{leagueId}/teams/{username}/roster", async (
    string leagueId, string username, RosterChangeRequest req, FirestoreDb db, PlayerCache players) =>
{
    var leagueDoc = db.Collection("leagues").Document(leagueId);
    var leagueSnap = await leagueDoc.GetSnapshotAsync();
    if (!leagueSnap.Exists)
        return Results.NotFound(new { error = "League not found." });
    var league = leagueSnap.ConvertTo<League>();

    var teamDoc = leagueDoc.Collection("teams").Document(Normalize(username));
    var teamSnap = await teamDoc.GetSnapshotAsync();
    if (!teamSnap.Exists)
        return Results.NotFound(new { error = "Team not found." });
    var team = teamSnap.ConvertTo<Team>();

    var player = (await players.GetByIdsAsync([req.PlayerId])).GetValueOrDefault(req.PlayerId);
    if (player is null)
        return Results.BadRequest(new { error = "Unknown player id." });
    if (team.PlayerIds.Contains(req.PlayerId))
        return Results.BadRequest(new { error = "Player already on this roster." });

    // One owner per player per league.
    var teamsSnap = await leagueDoc.Collection("teams").GetSnapshotAsync();
    var taken = teamsSnap.Documents.FirstOrDefault(d => d.ConvertTo<Team>().PlayerIds.Contains(req.PlayerId));
    if (taken is not null)
        return Results.Conflict(new { error = $"{player.FirstName} {player.LastName} is already on team '{taken.GetValue<string>("name")}'." });

    // Compensation: the displayed score must not move on a transaction.
    var totals = await PlayerTotalsSource.FetchAsync(db, [req.PlayerId], league.Season);
    var newPlayerPoints = ScoringEngine.PlayerPoints(totals[req.PlayerId], league.RuleConfig.PointValues);
    var rosterPositions = await RosterPositions(players, team.PlayerIds);
    var entriesBefore = team.PlayerIds
        .Select(id => (id, rosterPositions.GetValueOrDefault(id, "C"), team.PlayerPoints.GetValueOrDefault(id.ToString(), 0)))
        .ToList();
    var before = ScoringEngine.TeamScoreFromPoints(entriesBefore, league.RuleConfig.TopCount);
    var after = ScoringEngine.TeamScoreFromPoints(
        [.. entriesBefore, (req.PlayerId, player.Position, newPlayerPoints)], league.RuleConfig.TopCount);
    var delta = ScoringEngine.TransactionAdjustment(before.RawTopX, after.RawTopX);

    if (delta != 0)
        await teamDoc.Collection("adjustments").AddAsync(new Adjustment
        {
            Delta = delta,
            Reason = "add",
            PlayerIdsIn = [req.PlayerId],
            DateUtc = Timestamp.GetCurrentTimestamp(),
        });

    await leagueDoc.Collection("assignments").AddAsync(new Assignment
    {
        PlayerId = req.PlayerId,
        TeamUsername = team.OwnerUsername,
        From = EtToday(),
        Source = req.Source ?? "free_agency",
        SourceRefId = req.SourceRefId,
        CreatedUtc = Timestamp.GetCurrentTimestamp(),
    });

    var adjustmentsTotal = team.AdjustmentsTotal + delta;
    await teamDoc.UpdateAsync(new Dictionary<string, object>
    {
        ["playerIds"] = FieldValue.ArrayUnion(req.PlayerId),
        [$"playerPoints.{req.PlayerId}"] = newPlayerPoints,
        ["rawTopXScore"] = after.RawTopX,
        ["adjustmentsTotal"] = adjustmentsTotal,
        ["score"] = after.RawTopX + adjustmentsTotal,
        ["countedPlayerIds"] = after.CountedPlayerIds.ToList(),
    });
    return Results.Ok(PlayerDto.From(player));
});

app.MapDelete("/api/leagues/{leagueId}/teams/{username}/roster/{playerId:long}", async (
    string leagueId, string username, long playerId, FirestoreDb db, PlayerCache players) =>
{
    var leagueDoc = db.Collection("leagues").Document(leagueId);
    var leagueSnap = await leagueDoc.GetSnapshotAsync();
    if (!leagueSnap.Exists)
        return Results.NotFound(new { error = "League not found." });
    var league = leagueSnap.ConvertTo<League>();

    var teamDoc = leagueDoc.Collection("teams").Document(Normalize(username));
    var teamSnap = await teamDoc.GetSnapshotAsync();
    if (!teamSnap.Exists)
        return Results.NotFound(new { error = "Team not found." });
    var team = teamSnap.ConvertTo<Team>();
    if (!team.PlayerIds.Contains(playerId))
        return Results.BadRequest(new { error = "Player is not on this roster." });

    var rosterPositions = await RosterPositions(players, team.PlayerIds);
    var entriesBefore = team.PlayerIds
        .Select(id => (id, rosterPositions.GetValueOrDefault(id, "C"), team.PlayerPoints.GetValueOrDefault(id.ToString(), 0)))
        .ToList();
    var before = ScoringEngine.TeamScoreFromPoints(entriesBefore, league.RuleConfig.TopCount);
    var after = ScoringEngine.TeamScoreFromPoints(
        entriesBefore.Where(e => e.Item1 != playerId).ToList(), league.RuleConfig.TopCount);
    var delta = ScoringEngine.TransactionAdjustment(before.RawTopX, after.RawTopX);

    if (delta != 0)
        await teamDoc.Collection("adjustments").AddAsync(new Adjustment
        {
            Delta = delta,
            Reason = "drop",
            PlayerIdsOut = [playerId],
            DateUtc = Timestamp.GetCurrentTimestamp(),
        });

    // Close the open assignment, freezing its per-stint stats one final time
    // (they stop refreshing nightly the moment `to` is set).
    var openAssignments = await leagueDoc.Collection("assignments")
        .WhereEqualTo("playerId", playerId)
        .WhereEqualTo("teamUsername", team.OwnerUsername)
        .WhereEqualTo("to", null)
        .GetSnapshotAsync();
    var closeDate = EtToday();
    var closeNow = Timestamp.GetCurrentTimestamp();
    foreach (var assignment in openAssignments.Documents)
    {
        var a = assignment.ConvertTo<Assignment>();
        var lines = await PlayerTotalsSource.FetchLinesAsync(db, playerId);
        var finalTotals = PlayerTotalsSource.AggregateRange(lines, league.Season, a.From, closeDate);
        var finalFantasyPoints = ScoringEngine.PlayerPoints(finalTotals, league.RuleConfig.PointValues);
        var fields = AssignmentStats.ToFieldMap(finalTotals, finalFantasyPoints, closeNow);
        fields["to"] = closeDate;
        fields["closedUtc"] = closeNow;
        await assignment.Reference.UpdateAsync(fields);
    }

    var adjustmentsTotal = team.AdjustmentsTotal + delta;
    await teamDoc.UpdateAsync(new Dictionary<string, object>
    {
        ["playerIds"] = FieldValue.ArrayRemove(playerId),
        [$"playerPoints.{playerId}"] = FieldValue.Delete,
        ["rawTopXScore"] = after.RawTopX,
        ["adjustmentsTotal"] = adjustmentsTotal,
        ["score"] = after.RawTopX + adjustmentsTotal,
        ["countedPlayerIds"] = after.CountedPlayerIds.ToList(),
    });
    return Results.Ok();
});

app.MapGet("/api/leagues/{leagueId}/activity", async (string leagueId, int? limit, FirestoreDb db, PlayerCache players) =>
{
    var leagueDoc = db.Collection("leagues").Document(leagueId);
    var teamsSnap = await leagueDoc.Collection("teams").GetSnapshotAsync();
    var teamNames = teamsSnap.Documents.ToDictionary(d => d.Id, d => d.GetValue<string>("name"));

    var assignments = await leagueDoc.Collection("assignments").GetSnapshotAsync();
    var events = new List<(Timestamp When, string Type, long PlayerId, string TeamUsername, string Source, string? SourceRefId)>();
    foreach (var doc in assignments.Documents)
    {
        var a = doc.ConvertTo<Assignment>();
        events.Add((a.CreatedUtc, "add", a.PlayerId, a.TeamUsername, a.Source, a.SourceRefId));
        if (a.To is not null && a.ClosedUtc is { } closed)
            events.Add((closed, "drop", a.PlayerId, a.TeamUsername, a.Source, a.SourceRefId));
    }

    var take = Math.Clamp(limit ?? 15, 1, 50);
    var recent = events.OrderByDescending(e => e.When).Take(take).ToList();
    var playersById = await players.GetByIdsAsync(recent.Select(e => e.PlayerId));

    return Results.Ok(recent.Select(e =>
    {
        var player = playersById.GetValueOrDefault(e.PlayerId);
        return new
        {
            type = e.Type,
            dateUtc = e.When.ToDateTime(),
            playerId = e.PlayerId,
            playerName = player is null ? "Unknown player" : $"{player.FirstName} {player.LastName}",
            position = player?.Position,
            teamUsername = e.TeamUsername,
            teamName = teamNames.GetValueOrDefault(e.TeamUsername, e.TeamUsername),
            source = e.Source,
            sourceRefId = e.SourceRefId,
        };
    }));
});

app.MapGet("/api/players", async (string? q, PlayerCache players) =>
{
    var results = await players.SearchAsync(q ?? "", limit: 20);
    return Results.Ok(results.Select(PlayerDto.From));
});

app.MapGet("/api/leagues/{leagueId}/teams/{username}/season-stats", async (
    string leagueId, string username, FirestoreDb db, PlayerCache players) =>
{
    var leagueSnap = await db.Collection("leagues").Document(leagueId).GetSnapshotAsync();
    if (!leagueSnap.Exists)
        return Results.NotFound(new { error = "League not found." });
    var league = leagueSnap.ConvertTo<League>();

    var teamSnap = await leagueSnap.Reference.Collection("teams").Document(Normalize(username)).GetSnapshotAsync();
    if (!teamSnap.Exists)
        return Results.NotFound(new { error = "Team not found." });
    var team = teamSnap.ConvertTo<Team>();

    var playersById = await players.GetByIdsAsync(team.PlayerIds);
    var totalsById = await PlayerTotalsSource.FetchWithCacheAsync(db, team.PlayerIds, league.Season);

    // Pool group's stats are scoped to the *current* assignment (this stint
    // with this owner), precomputed nightly by ScoreCalcJob — never
    // recomputed live here.
    var openAssignmentsByPlayer = (await leagueSnap.Reference.Collection("assignments")
            .WhereEqualTo("teamUsername", Normalize(username))
            .WhereEqualTo("to", null)
            .GetSnapshotAsync())
        .Documents.Select(d => d.ConvertTo<Assignment>())
        .ToDictionary(a => a.PlayerId);

    var rows = team.PlayerIds
        .Select(id => playersById.GetValueOrDefault(id))
        .Where(p => p is not null)
        .Select(p =>
        {
            var t = totalsById.GetValueOrDefault(p!.NhlId) ?? new PlayerRawTotals();
            var isGoalie = p.Position == "G";
            var assignment = openAssignmentsByPlayer.GetValueOrDefault(p.NhlId);
            return new
            {
                id = p.NhlId,
                name = $"{p.FirstName} {p.LastName}",
                position = p.Position,
                team = p.TeamAbbrev,
                capHit = p.CapHit,
                headshotUrl = p.HeadshotUrl,
                isGoalie,
                gamesPlayed = t.GamesPlayed,
                goals = t.Goals,
                assists = t.Assists,
                points = t.Goals + t.Assists,
                plusMinus = t.PlusMinus,
                pim = t.Pim,
                shots = t.Shots,
                hits = t.Hits,
                blockedShots = t.BlockedShots,
                wins = t.Wins,
                otLosses = t.OtLosses,
                shutouts = t.Shutouts,
                goalsAgainst = t.GoalsAgainst,
                saves = t.Saves,
                shotsAgainst = t.ShotsAgainst,
                assignmentFrom = assignment?.From,
                assignmentGamesPlayed = assignment?.GamesPlayed ?? 0,
                assignmentFantasyPoints = assignment?.FantasyPoints ?? 0,
            };
        })
        .ToList();

    return Results.Ok(new { season = league.Season, players = rows });
});

app.MapGet("/api/players/{playerId:long}", async (long playerId, FirestoreDb db, PlayerCache players) =>
{
    var player = (await players.GetByIdsAsync([playerId])).GetValueOrDefault(playerId);
    if (player is null)
        return Results.NotFound(new { error = "Player not found." });

    var lines = await PlayerTotalsSource.FetchLinesAsync(db, playerId);
    var season = lines.Count > 0 ? lines.Max(l => l.Season) : null;
    var seasonLines = lines.Where(l => l.Season == season && l.GameType == 2).ToList();
    var totals = PlayerTotalsSource.Aggregate(seasonLines);
    var isGoalie = player.Position == "G";

    // Already computed for free (the lines above are fetched for recentGames
    // regardless) — refresh the consolidated cache as a side effect.
    if (season is not null)
        await PlayerTotalsSource.CacheAsync(db, season, playerId, totals);

    return Results.Ok(new
    {
        id = player.NhlId,
        name = $"{player.FirstName} {player.LastName}",
        position = player.Position,
        team = player.TeamAbbrev,
        status = player.Status,
        sweaterNumber = player.SweaterNumber,
        shootsCatches = player.ShootsCatches,
        birthDate = player.BirthDate,
        birthCountry = player.BirthCountry,
        heightCm = player.HeightCm,
        weightKg = player.WeightKg,
        headshotUrl = player.HeadshotUrl,
        capHit = player.CapHit,
        isGoalie,
        season,
        seasonTotals = new
        {
            gamesPlayed = totals.GamesPlayed,
            goals = totals.Goals,
            assists = totals.Assists,
            points = totals.Goals + totals.Assists,
            plusMinus = totals.PlusMinus,
            pim = totals.Pim,
            shots = totals.Shots,
            wins = totals.Wins,
            otLosses = totals.OtLosses,
            shutouts = totals.Shutouts,
            goalsAgainst = totals.GoalsAgainst,
            saves = totals.Saves,
            shotsAgainst = totals.ShotsAgainst,
        },
        recentGames = seasonLines
            .OrderByDescending(l => l.Date)
            .Take(10)
            .Select(l => new
            {
                date = l.Date,
                gameId = l.GameId,
                opponent = l.OpponentAbbrev,
                isHome = l.IsHome,
                goals = l.Goals,
                assists = l.Assists,
                points = l.Points,
                plusMinus = l.PlusMinus,
                pim = l.Pim,
                shots = l.Shots,
                toi = l.Toi,
                decision = l.Decision,
                saves = l.Saves,
                shotsAgainst = l.ShotsAgainst,
                goalsAgainst = l.GoalsAgainst,
                shutout = l.Shutout,
            }),
    });
});

app.Run();

static double? PtsPerGame(Team team, Dictionary<long, PlayerRawTotals> totalsById)
{
    var gamesPlayed = team.PlayerIds.Sum(id => totalsById.GetValueOrDefault(id)?.GamesPlayed ?? 0);
    return gamesPlayed > 0 ? Math.Round(team.Score / (double)gamesPlayed, 2) : null;
}

static async Task<Dictionary<long, string>> RosterPositions(PlayerCache players, IReadOnlyCollection<long> playerIds)
{
    var byId = await players.GetByIdsAsync(playerIds);
    return byId.ToDictionary(kv => kv.Key, kv => kv.Value.Position);
}

record LoginRequest(string? Username);
record CreateLeagueRequest(string? Name, string? Username, string? TeamName, string? Season, long? CapAmount);
record JoinLeagueRequest(string? Username, string? TeamName);
record RosterChangeRequest(long PlayerId, string? Source, string? SourceRefId);
record UpdateRulesRequest(string? Username, RuleConfig? RuleConfig);

record PlayerDto(long Id, string Name, string Position, string Team, string Status, long? CapHit, string? HeadshotUrl)
{
    public static PlayerDto From(Player p) =>
        new(p.NhlId, $"{p.FirstName} {p.LastName}", p.Position, p.TeamAbbrev, p.Status, p.CapHit, p.HeadshotUrl);
}
