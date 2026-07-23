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

    var positions = await RosterPositions(players, [.. team.PlayerIds, req.PlayerId]);
    await RosterChange.ApplyAsync(
        db, leagueDoc, league, teamDoc, team, positions,
        playersOut: [], playersIn: [req.PlayerId],
        adjustmentReason: "add",
        creationEvent: req.CreationEvent ?? AssignmentCreationEvent.FreeAgent, creationEventReferenceId: req.CreationEventReferenceId,
        closeReason: AssignmentCloseReason.Release, closeReasonReferenceId: null,
        effectiveDate: EtToday());
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

    var positions = await RosterPositions(players, team.PlayerIds);
    await RosterChange.ApplyAsync(
        db, leagueDoc, league, teamDoc, team, positions,
        playersOut: [playerId], playersIn: [],
        adjustmentReason: "drop",
        creationEvent: AssignmentCreationEvent.FreeAgent, creationEventReferenceId: null,
        closeReason: AssignmentCloseReason.Release, closeReasonReferenceId: null,
        effectiveDate: EtToday());
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
        events.Add((a.CreatedUtc, "add", a.PlayerId, a.TeamUsername, a.CreationEvent, a.CreationEventReferenceId));
        // Why it closed (e.g. "trade") can differ from why it was opened
        // (e.g. "freeagent") — a freeagent-acquired assignment can end via a trade.
        if (a.To is not null && a.ClosedUtc is { } closed)
            events.Add((closed, "drop", a.PlayerId, a.TeamUsername, a.CloseReason ?? a.CreationEvent, a.CloseReasonReferenceId ?? a.CreationEventReferenceId));
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

app.MapPost("/api/leagues/{leagueId}/trades", async (
    string leagueId, ProposeTradeRequest req, FirestoreDb db) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.CounterpartyUsername))
        return Results.BadRequest(new { error = "Username and counterpartyUsername are required." });
    var proposer = Normalize(req.Username);
    var counterparty = Normalize(req.CounterpartyUsername);
    if (proposer == counterparty)
        return Results.BadRequest(new { error = "Cannot trade with yourself." });

    var playersFromProposer = req.PlayersFromProposer ?? [];
    var playersFromCounterparty = req.PlayersFromCounterparty ?? [];
    if (playersFromProposer.Count == 0 && playersFromCounterparty.Count == 0)
        return Results.BadRequest(new { error = "Trade must include at least one player." });
    if (TradeValidation.HasOverlap(playersFromProposer, playersFromCounterparty))
        return Results.BadRequest(new { error = "A player can't appear on both sides of a trade." });

    var leagueDoc = db.Collection("leagues").Document(leagueId);
    if (!(await leagueDoc.GetSnapshotAsync()).Exists)
        return Results.NotFound(new { error = "League not found." });

    var proposerSnap = await leagueDoc.Collection("teams").Document(proposer).GetSnapshotAsync();
    var counterpartySnap = await leagueDoc.Collection("teams").Document(counterparty).GetSnapshotAsync();
    if (!proposerSnap.Exists || !counterpartySnap.Exists)
        return Results.NotFound(new { error = "Team not found." });
    var proposerTeam = proposerSnap.ConvertTo<Team>();
    var counterpartyTeam = counterpartySnap.ConvertTo<Team>();

    if (playersFromProposer.Except(proposerTeam.PlayerIds).Any())
        return Results.BadRequest(new { error = "You can only offer players on your own roster." });
    if (playersFromCounterparty.Except(counterpartyTeam.PlayerIds).Any())
        return Results.BadRequest(new { error = "Requested players aren't on that team's roster." });

    var tradeRef = await leagueDoc.Collection("trades").AddAsync(new Trade
    {
        ProposerUsername = proposer,
        CounterpartyUsername = counterparty,
        PlayersFromProposer = playersFromProposer,
        PlayersFromCounterparty = playersFromCounterparty,
        Status = TradeStatus.Pending,
        CreatedUtc = Timestamp.GetCurrentTimestamp(),
    });
    return Results.Ok(new { id = tradeRef.Id });
});

app.MapPost("/api/leagues/{leagueId}/trades/{tradeId}/respond", async (
    string leagueId, string tradeId, RespondTradeRequest req, FirestoreDb db) =>
{
    if (string.IsNullOrWhiteSpace(req.Username))
        return Results.BadRequest(new { error = "Username is required." });
    var username = Normalize(req.Username);

    var tradeDoc = db.Collection("leagues").Document(leagueId).Collection("trades").Document(tradeId);
    var tradeSnap = await tradeDoc.GetSnapshotAsync();
    if (!tradeSnap.Exists)
        return Results.NotFound(new { error = "Trade not found." });
    var trade = tradeSnap.ConvertTo<Trade>();

    if (req.Accept)
    {
        if (!TradeValidation.CanAccept(trade, username))
            return Results.Json(new { error = "Only the receiving team can accept this trade." }, statusCode: 403);
        await tradeDoc.UpdateAsync(new Dictionary<string, object>
        {
            ["status"] = TradeStatus.Accepted,
            ["respondedUtc"] = Timestamp.GetCurrentTimestamp(),
        });
        return Results.Ok(new { ok = true, status = TradeStatus.Accepted, note = "Processed the night after the next scoring update." });
    }

    // Declining branches by who's acting: the proposer withdrawing their own
    // offer is "cancelled"; the counterparty rejecting someone else's offer
    // is "declined" — distinct statuses so the status alone says who acted.
    if (username == trade.ProposerUsername)
    {
        if (!TradeValidation.CanCancel(trade, username))
            return Results.Json(new { error = "This offer can no longer be cancelled." }, statusCode: 403);
        await tradeDoc.UpdateAsync(new Dictionary<string, object>
        {
            ["status"] = TradeStatus.Cancelled,
            ["respondedUtc"] = Timestamp.GetCurrentTimestamp(),
        });
        return Results.Ok(new { ok = true, status = TradeStatus.Cancelled });
    }
    if (!TradeValidation.CanDecline(trade, username))
        return Results.Json(new { error = "Only the receiving team can decline this trade." }, statusCode: 403);
    await tradeDoc.UpdateAsync(new Dictionary<string, object>
    {
        ["status"] = TradeStatus.Declined,
        ["respondedUtc"] = Timestamp.GetCurrentTimestamp(),
    });
    return Results.Ok(new { ok = true, status = TradeStatus.Declined });
});

app.MapGet("/api/leagues/{leagueId}/trades", async (string leagueId, string? username, FirestoreDb db, PlayerCache players) =>
{
    var leagueDoc = db.Collection("leagues").Document(leagueId);
    if (!(await leagueDoc.GetSnapshotAsync()).Exists)
        return Results.NotFound(new { error = "League not found." });
    if (string.IsNullOrWhiteSpace(username))
        return Results.BadRequest(new { error = "username is required." });
    var normalizedViewer = Normalize(username);

    var teamsSnap = await leagueDoc.Collection("teams").GetSnapshotAsync();
    var teamNames = teamsSnap.Documents.ToDictionary(d => d.Id, d => d.GetValue<string>("name"));

    var tradesSnap = await leagueDoc.Collection("trades").GetSnapshotAsync();
    var trades = tradesSnap.Documents
        .Select(d => (Doc: d, Trade: d.ConvertTo<Trade>()))
        // pending/declined/cancelled trades are private to the two parties;
        // accepted/processed are visible to the whole league.
        .Where(t => TradeValidation.IsVisibleTo(t.Trade, normalizedViewer))
        .OrderByDescending(t => t.Trade.CreatedUtc.ToDateTime())
        .ToList();

    var allPlayerIds = trades.SelectMany(t => t.Trade.PlayersFromProposer.Concat(t.Trade.PlayersFromCounterparty)).Distinct();
    var playersById = await players.GetByIdsAsync(allPlayerIds);

    List<object> PlayerList(List<long> ids) => ids.Select(id =>
    {
        var p = playersById.GetValueOrDefault(id);
        return (object)new { id, name = p is null ? "Unknown player" : $"{p.FirstName} {p.LastName}", position = p?.Position };
    }).ToList();

    var result = new List<object>();
    foreach (var (doc, trade) in trades)
    {
        object? myVote = null;
        var tally = new { proposerClear = 0, proposerLean = 0, fair = 0, counterpartyLean = 0, counterpartyClear = 0, total = 0 };
        if (trade.Status == TradeStatus.Processed)
        {
            var votesSnap = await doc.Reference.Collection("votes").GetSnapshotAsync();
            var votes = votesSnap.Documents.Select(v => (Id: v.Id, Vote: v.ConvertTo<TradeVote>())).ToList();
            int CountWhere(Func<TradeVote, bool> pred) => votes.Count(v => pred(v.Vote));
            tally = new
            {
                proposerClear = CountWhere(v => v.FavoredUsername == trade.ProposerUsername && v.Magnitude == 2),
                proposerLean = CountWhere(v => v.FavoredUsername == trade.ProposerUsername && v.Magnitude == 1),
                fair = CountWhere(v => v.FavoredUsername is null),
                counterpartyLean = CountWhere(v => v.FavoredUsername == trade.CounterpartyUsername && v.Magnitude == 1),
                counterpartyClear = CountWhere(v => v.FavoredUsername == trade.CounterpartyUsername && v.Magnitude == 2),
                total = votes.Count,
            };
            var mine = votes.FirstOrDefault(v => v.Id == normalizedViewer);
            if (mine.Vote is not null)
                myVote = new { favoredUsername = mine.Vote.FavoredUsername, magnitude = mine.Vote.Magnitude };
        }

        result.Add(new
        {
            id = doc.Id,
            proposerUsername = trade.ProposerUsername,
            proposerTeamName = teamNames.GetValueOrDefault(trade.ProposerUsername, trade.ProposerUsername),
            counterpartyUsername = trade.CounterpartyUsername,
            counterpartyTeamName = teamNames.GetValueOrDefault(trade.CounterpartyUsername, trade.CounterpartyUsername),
            playersFromProposer = PlayerList(trade.PlayersFromProposer),
            playersFromCounterparty = PlayerList(trade.PlayersFromCounterparty),
            status = trade.Status,
            createdUtc = trade.CreatedUtc.ToDateTime(),
            respondedUtc = trade.RespondedUtc?.ToDateTime(),
            processedUtc = trade.ProcessedUtc?.ToDateTime(),
            votes = tally,
            myVote,
        });
    }
    return Results.Ok(result);
});

app.MapPost("/api/leagues/{leagueId}/trades/{tradeId}/vote", async (
    string leagueId, string tradeId, VoteTradeRequest req, FirestoreDb db) =>
{
    if (string.IsNullOrWhiteSpace(req.Username))
        return Results.BadRequest(new { error = "Username is required." });

    var tradeDoc = db.Collection("leagues").Document(leagueId).Collection("trades").Document(tradeId);
    var tradeSnap = await tradeDoc.GetSnapshotAsync();
    if (!tradeSnap.Exists)
        return Results.NotFound(new { error = "Trade not found." });
    var trade = tradeSnap.ConvertTo<Trade>();
    if (!TradeValidation.CanVoteOnTrade(trade))
        return Results.BadRequest(new { error = "Only processed trades can be rated." });
    if (!TradeValidation.IsValidVote(req.FavoredUsername, req.Magnitude, trade.ProposerUsername, trade.CounterpartyUsername))
        return Results.BadRequest(new { error = "Invalid vote: favoredUsername must be null (fair) or one of the two teams, with a matching magnitude (0 for fair, 1-2 otherwise)." });

    var username = Normalize(req.Username);
    await tradeDoc.Collection("votes").Document(username).SetAsync(new TradeVote
    {
        FavoredUsername = req.FavoredUsername,
        Magnitude = req.Magnitude,
        VotedUtc = Timestamp.GetCurrentTimestamp(),
    });
    return Results.Ok(new { ok = true });
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
record RosterChangeRequest(long PlayerId, string? CreationEvent, string? CreationEventReferenceId);
record UpdateRulesRequest(string? Username, RuleConfig? RuleConfig);
record ProposeTradeRequest(string? Username, string? CounterpartyUsername, List<long>? PlayersFromProposer, List<long>? PlayersFromCounterparty);
record RespondTradeRequest(string? Username, bool Accept);
record VoteTradeRequest(string? Username, string? FavoredUsername, int Magnitude);

record PlayerDto(long Id, string Name, string Position, string Team, string Status, long? CapHit, string? HeadshotUrl)
{
    public static PlayerDto From(Player p) =>
        new(p.NhlId, $"{p.FirstName} {p.LastName}", p.Position, p.TeamAbbrev, p.Status, p.CapHit, p.HeadshotUrl);
}
