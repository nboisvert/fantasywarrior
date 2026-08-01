using FantasyWarrior.Api;
using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Lineups;
using FantasyWarrior.Core.News;
using FantasyWarrior.Core.Periods;
using FantasyWarrior.Core.Players;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Core.Users;
using Google.Cloud.Firestore;

// TEMPORARY AUTH MODEL: the API trusts the username sent by the client.
// Firebase Auth token verification replaces this when real auth lands.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton<FirestoreDb>(_ =>
    FirestoreDb.Create(Environment.GetEnvironmentVariable("FIRESTORE_PROJECT_ID") ?? "fantasywarriordb"));
builder.Services.AddSingleton<PlayerCache>();
// Resolves the simulation cursor when a season replay is running, the real
// clock otherwise. Singleton with its own short TTL so this costs at most one
// Firestore read every few seconds rather than one per request.
builder.Services.AddSingleton<SimulationClock>(sp => new SimulationClock(sp.GetRequiredService<FirestoreDb>()));

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => "Fantasy Warrior API");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Reports whether a season replay is in progress and what day it thinks it is.
// The one place to look when the app's idea of "this week" seems wrong.
app.MapGet("/api/clock", async (SimulationClock clock) =>
{
    var state = await clock.StateAsync();
    var now = await clock.NowAsync();
    return Results.Ok(new
    {
        simulated = state is not null,
        asOfDate = state?.AsOfDate,
        season = state?.Season,
        todayEt = PoolClock.TodayEtIso(now),
        lastStatDate = PoolClock.LastStatDateIso(now),
    });
});

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

    // An unrecognised stat key would score zero forever, silently, and read as
    // a scoring bug rather than a typo — so it is rejected here, not absorbed.
    var errors = RuleConfigValidation.Validate(req.RuleConfig);
    if (errors.Count > 0)
        return Results.BadRequest(new { error = string.Join(" ", errors), errors });

    await leagueDoc.UpdateAsync("ruleConfig", req.RuleConfig);
    return Results.Ok(new
    {
        ok = true,
        note = "Applies from the next nightly scoring run. Weeks already banked keep the "
             + "scale they were scored under — run `recompute` to restate them.",
    });
});

// Light league read: team-level rows for all teams (everything precomputed on
// the team doc — no per-player season-stats loop, no full PlayerCache load) plus
// ONLY the requesting user's own roster, fetched with a direct batched read of
// just those player docs. Other teams' rosters/stats are loaded on demand by
// their own screens (Stats, CreateTradeSheet).
app.MapGet("/api/leagues/{leagueId}", async (string leagueId, string? username, FirestoreDb db, SimulationClock clock) =>
{
    var leagueSnap = await db.Collection("leagues").Document(leagueId).GetSnapshotAsync();
    if (!leagueSnap.Exists)
        return Results.NotFound(new { error = "League not found." });

    var league = leagueSnap.ConvertTo<League>();
    var teamsSnap = await leagueSnap.Reference.Collection("teams").GetSnapshotAsync();
    var teams = teamsSnap.Documents.Select(d => d.ConvertTo<Team>()).ToList();

    var simNow = (await clock.NowAsync()).UtcDateTime;
    var (currentPeriod, _) = await LineupEndpoints.ResolvePeriodAsync(db, league.Season, null, clock);

    var normalized = username is null ? null : Normalize(username);
    var myTeam = normalized is null ? null : teams.FirstOrDefault(t => t.OwnerUsername == normalized);
    var myRosterIds = myTeam?.PlayerIds ?? [];

    var myPlayersById = new Dictionary<long, Player>();
    if (myRosterIds.Count > 0)
    {
        var refs = myRosterIds.Select(id => db.Collection("players").Document(id.ToString())).ToArray();
        var playerSnaps = await db.GetAllSnapshotsAsync(refs);
        myPlayersById = playerSnaps.Where(s => s.Exists)
            .Select(s => s.ConvertTo<Player>())
            .ToDictionary(p => p.NhlId);
    }

    return Results.Ok(new
    {
        id = leagueSnap.Id,
        league.Name,
        league.Season,
        league.CapAmount,
        league.CommissionerUsername,
        league.RuleConfig,
        members = league.MemberUsernames,
        // Additive for now: the old fields stay until the frontend has moved
        // over, because Pages deploys on push while Cloud Run is manual --
        // removing a field in the same commit the client stops reading it
        // would break whichever ships first.
        currentPeriod = currentPeriod is null ? null : new
        {
            index = currentPeriod.Index,
            startDate = currentPeriod.StartDate,
            endDate = currentPeriod.EndDate,
            gameCount = currentPeriod.GameCount,
            locked = currentPeriod.LockUtc.ToDateTime() <= simNow,
            finalized = currentPeriod.FinalizedUtc is not null,
        },
        teams = teams
            .OrderByDescending(t => t.Score)
            .ThenBy(t => t.Name)
            .Select(t => new
            {
                t.Name,
                t.OwnerUsername,
                t.Score,
                ptsPerGame = t.RosterGamesPlayed > 0 ? Math.Round(t.Score / t.RosterGamesPlayed, 2) : (double?)null,
                capTotal = t.CapTotal,
                playerCount = t.PlayerIds.Count,
                playerNhlPoints = t.PlayerNhlPoints,
                franchiseAbbrev = t.FranchiseAbbrev,
                // weekly-lineup scoring
                periodPoints = t.PeriodPoints,
                benchScore = t.BenchScore,
                finalizedScore = t.FinalizedScore,
            }),
        myRoster = myRosterIds
            .Select(id => myPlayersById.GetValueOrDefault(id))
            .Where(p => p is not null)
            .Select(p => new
            {
                Dto = PlayerDto.From(p!),
                Points = myTeam!.PlayerPoints.GetValueOrDefault(p!.NhlId.ToString(), 0),
                NhlPoints = myTeam.PlayerNhlPoints.GetValueOrDefault(p.NhlId.ToString(), 0),
            })
            .OrderByDescending(x => x.Points)
            .Select(x => new
            {
                x.Dto.Id, x.Dto.Name, x.Dto.Position, x.Dto.Team, x.Dto.Status,
                x.Dto.CapHit, x.Dto.HeadshotUrl, x.Points, x.NhlPoints,
            }),
    });
});

app.MapPost("/api/leagues/{leagueId}/teams/{username}/roster", async (
    string leagueId, string username, RosterChangeRequest req, FirestoreDb db, PlayerCache players, SimulationClock clock) =>
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
        startReason: req.CreationEvent ?? RosterSpotReason.FreeAgent, startRefId: req.CreationEventReferenceId,
        endReason: RosterSpotReason.Release, endRefId: null,
        effectiveDate: await clock.TodayEtAsync());
    return Results.Ok(PlayerDto.From(player));
});

app.MapDelete("/api/leagues/{leagueId}/teams/{username}/roster/{playerId:long}", async (
    string leagueId, string username, long playerId, FirestoreDb db, PlayerCache players, SimulationClock clock) =>
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
        startReason: RosterSpotReason.FreeAgent, startRefId: null,
        endReason: RosterSpotReason.Release, endRefId: null,
        effectiveDate: await clock.TodayEtAsync());
    return Results.Ok();
});

app.MapGet("/api/news", async (int? limit, FirestoreDb db) =>
{
    var take = Math.Clamp(limit ?? 30, 1, 50);
    var newsSnap = await db.Collection("news")
        .OrderByDescending("publishedUtc")
        .Limit(take)
        .GetSnapshotAsync();

    return Results.Ok(newsSnap.Documents.Select(doc =>
    {
        var item = doc.ConvertTo<NewsItem>();
        return new
        {
            id = doc.Id,
            source = item.Source,
            headline = item.Headline,
            url = item.Url,
            playerId = item.PlayerId,
            playerName = item.PlayerName,
            publishedUtc = item.PublishedUtc.ToDateTime(),
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

// --- Weekly lineup -------------------------------------------------------
// The one place in the app where a rule is actually enforced rather than
// merely displayed: roster size and the salary cap are still advisory.

app.MapGet("/api/leagues/{leagueId}/teams/{username}/lineup", async (
    string leagueId, string username, int? period, string? viewer, FirestoreDb db, PlayerCache players, SimulationClock clock) =>
{
    var leagueSnap = await db.Collection("leagues").Document(leagueId).GetSnapshotAsync();
    if (!leagueSnap.Exists) return Results.NotFound(new { error = "League not found." });
    var league = leagueSnap.ConvertTo<League>();

    var owner = Normalize(username);
    var teamSnap = await leagueSnap.Reference.Collection("teams").Document(owner).GetSnapshotAsync();
    if (!teamSnap.Exists) return Results.NotFound(new { error = "Team not found." });

    var (periodDoc, allPeriods) = await LineupEndpoints.ResolvePeriodAsync(db, league.Season, period, clock);
    if (periodDoc is null) return Results.NotFound(new { error = "No period calendar for this season." });

    var locked = periodDoc.LockUtc.ToDateTime() <= (await clock.NowAsync()).UtcDateTime;

    // A rival's lineup is competitive information until it locks.
    var isOwner = viewer is not null && Normalize(viewer) == owner;
    if (!isOwner && !locked)
        return Results.Ok(new
        {
            periodIndex = periodDoc.Index, startDate = periodDoc.StartDate, endDate = periodDoc.EndDate,
            gameCount = periodDoc.GameCount, locked, finalized = periodDoc.FinalizedUtc is not null,
            isOwner = false, hidden = true,
            slots = LineupEndpoints.SlotsDto(league.RuleConfig), used = new { },
            entries = Array.Empty<object>(),
            periods = allPeriods,
        });

    var spots = await RosterSpots.OpenForTeamAsync(leagueSnap.Reference, owner);
    var lineupSnap = await leagueSnap.Reference.Collection("lineups")
        .Document(Lineup.DocId(owner, periodDoc.Index)).GetSnapshotAsync();
    var lineup = lineupSnap.Exists ? lineupSnap.ConvertTo<Lineup>() : null;

    var slots = LineupRules.SlotsFrom(league.RuleConfig);
    var candidates = spots.Select(s => new LineupCandidate(
        s.Ref.Id, s.Spot.PlayerId, s.Spot.PositionGroup, s.Spot.ActivePoints, s.Spot.OpenedUtc.ToDateTime())).ToList();
    var active = lineup is not null
        ? LineupRules.LegalActiveSet(candidates, lineup.ActiveSpotIds, slots)
        : new HashSet<string>();

    var playersById = await players.GetByIdsAsync([.. spots.Select(s => s.Spot.PlayerId)]);

    return Results.Ok(new
    {
        periodIndex = periodDoc.Index, startDate = periodDoc.StartDate, endDate = periodDoc.EndDate,
        gameCount = periodDoc.GameCount, locked, finalized = periodDoc.FinalizedUtc is not null,
        isOwner, hidden = false,
        setBy = lineup?.SetBy, submittedUtc = lineup?.SubmittedUtc?.ToDateTime(),
        activePoints = lineup?.ActivePoints ?? 0, benchPoints = lineup?.BenchPoints ?? 0,
        slots = LineupEndpoints.SlotsDto(league.RuleConfig),
        used = LineupRules.Used(candidates, [.. active]),
        entries = spots.Select(s =>
        {
            var p = playersById.GetValueOrDefault(s.Spot.PlayerId);
            var result = lineup?.Results.GetValueOrDefault(s.Ref.Id);
            return new
            {
                spotId = s.Ref.Id,
                playerId = s.Spot.PlayerId,
                name = p is null ? "Unknown player" : $"{p.FirstName} {p.LastName}",
                position = p?.Position ?? s.Spot.Position,
                positionGroup = s.Spot.PositionGroup,
                team = p?.TeamAbbrev,
                headshotUrl = p?.HeadshotUrl,
                capHit = p?.CapHit,
                active = active.Contains(s.Ref.Id),
                points = result?.Points ?? 0,
                gamesPlayed = result?.GamesPlayed ?? 0,
                fromDate = result?.FromDate,
                toDate = result?.ToDate,
                seasonPoints = s.Spot.ActivePoints,
            };
        })
        .OrderBy(e => e.positionGroup == "F" ? 0 : e.positionGroup == "D" ? 1 : 2)
        .ThenByDescending(e => e.active)
        .ThenByDescending(e => e.seasonPoints),
        periods = allPeriods,
    });
});

app.MapMethods("/api/leagues/{leagueId}/teams/{username}/lineup", ["PUT"], async (
    string leagueId, string username, SetLineupRequest req, FirestoreDb db, SimulationClock clock) =>
{
    if (string.IsNullOrWhiteSpace(req.Username))
        return Results.BadRequest(new { error = "Username is required." });

    var owner = Normalize(username);
    // No real auth yet, so this only stops accidents, not attacks. Silently
    // benching a rival's best player every Sunday would be undetectable --
    // this endpoint wants a Firebase token check before real users touch it.
    if (Normalize(req.Username) != owner)
        return Results.Json(new { error = "You can only set your own lineup." }, statusCode: 403);

    var leagueSnap = await db.Collection("leagues").Document(leagueId).GetSnapshotAsync();
    if (!leagueSnap.Exists) return Results.NotFound(new { error = "League not found." });
    var league = leagueSnap.ConvertTo<League>();

    var teamSnap = await leagueSnap.Reference.Collection("teams").Document(owner).GetSnapshotAsync();
    if (!teamSnap.Exists) return Results.NotFound(new { error = "Team not found." });

    var (periodDoc, _) = await LineupEndpoints.ResolvePeriodAsync(db, league.Season, req.PeriodIndex, clock);
    if (periodDoc is null) return Results.NotFound(new { error = "Period not found." });

    if (periodDoc.LockUtc.ToDateTime() <= (await clock.NowAsync()).UtcDateTime)
        return Results.Conflict(new { error = $"Week {periodDoc.Index} is locked. Set your lineup for the next week instead." });

    var spots = await RosterSpots.OpenForTeamAsync(leagueSnap.Reference, owner);
    var candidates = spots.Select(s => new LineupCandidate(
        s.Ref.Id, s.Spot.PlayerId, s.Spot.PositionGroup, s.Spot.ActivePoints, s.Spot.OpenedUtc.ToDateTime())).ToList();
    var requested = req.ActiveSpotIds ?? [];

    var errors = LineupRules.Validate(candidates, requested, LineupRules.SlotsFrom(league.RuleConfig));
    if (errors.Count > 0) return Results.BadRequest(new { error = string.Join(" ", errors), errors });

    // The whole active set is one field, so this single write is atomic --
    // two tabs racing cannot produce an illegal roster.
    var lineupRef = leagueSnap.Reference.Collection("lineups").Document(Lineup.DocId(owner, periodDoc.Index));
    await lineupRef.SetAsync(new Dictionary<string, object>
    {
        ["teamUsername"] = owner,
        ["periodIndex"] = periodDoc.Index,
        ["season"] = league.Season,
        ["activeSpotIds"] = requested.Distinct().ToList(),
        ["submittedUtc"] = Timestamp.GetCurrentTimestamp(),
        ["setBy"] = owner,
    }, SetOptions.MergeAll);

    return Results.Ok(new { ok = true, periodIndex = periodDoc.Index, active = requested.Distinct().Count() });
});

app.MapGet("/api/leagues/{leagueId}/teams/{username}/season-stats", async (
    string leagueId, string username, FirestoreDb db, PlayerCache players, SimulationClock clock) =>
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
    var totalsById = await PlayerTotalsSource.FetchWithCacheAsync(
        db, team.PlayerIds, league.Season, (await clock.StateAsync())?.AsOfDate);

    // Pool-group stats are scoped to this player's current stint with this
    // team and count only the weeks he was in the active lineup. Precomputed
    // by the nightly rollup — never recomputed live here.
    var spotsByPlayer = (await RosterSpots.OpenForTeamAsync(leagueSnap.Reference, Normalize(username)))
        .GroupBy(s => s.Spot.PlayerId)
        .ToDictionary(g => g.Key, g => g.First().Spot);

    var rows = team.PlayerIds
        .Select(id => playersById.GetValueOrDefault(id))
        .Where(p => p is not null)
        .Select(p =>
        {
            var t = totalsById.GetValueOrDefault(p!.NhlId) ?? new PlayerRawTotals();
            var isGoalie = p.Position == "G";
            var spot = spotsByPlayer.GetValueOrDefault(p.NhlId);
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
                
                spotStartDate = spot?.StartDate,
                spotActiveGamesPlayed = spot?.ActiveGamesPlayed ?? 0,
                spotActiveGoals = spot?.Stats()[StatKeys.Goals] ?? 0,
                spotActiveAssists = spot?.Stats()[StatKeys.Assists] ?? 0,
                spotActivePoints = spot?.ActivePoints ?? 0,
                spotBenchPoints = spot?.BenchPoints ?? 0,
            };
        })
        .ToList();

    return Results.Ok(new { season = league.Season, players = rows });
});

app.MapGet("/api/players/{playerId:long}", async (long playerId, FirestoreDb db, PlayerCache players, SimulationClock clock) =>
{
    var player = (await players.GetByIdsAsync([playerId])).GetValueOrDefault(playerId);
    if (player is null)
        return Results.NotFound(new { error = "Player not found." });

    // During a season replay the card must show what was known on the simulated
    // day — otherwise a November card reports April totals and lists games that
    // haven't been played yet.
    var throughDate = (await clock.StateAsync())?.AsOfDate;

    var lines = await PlayerTotalsSource.FetchLinesAsync(db, playerId);
    var season = lines.Count > 0 ? lines.Max(l => l.Season) : null;
    var seasonLines = lines
        .Where(l => l.Season == season && l.GameType == 2
            && (throughDate is null || string.CompareOrdinal(l.Date, throughDate) <= 0))
        .ToList();
    var totals = PlayerTotalsSource.Aggregate(seasonLines);
    var isGoalie = player.Position == "G";

    // This used to refresh playerSeasonStats as a "free" side effect. It was a
    // write on every card view, and — now that the cache carries a throughDate —
    // an actively harmful one: it would overwrite a replay-bounded entry with
    // whole-season totals. The endpoint computes its response from the lines
    // above and never reads the cache, so nothing is lost by not writing it.
    // The nightly job owns that cache.

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
        draftYear = player.DraftYear,
        draftRound = player.DraftRound,
        draftOverall = player.DraftOverall,
        draftTeamAbbrev = player.DraftTeamAbbrev,
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

static async Task<Dictionary<long, string>> RosterPositions(PlayerCache players, IReadOnlyCollection<long> playerIds)
{
    var byId = await players.GetByIdsAsync(playerIds);
    return byId.ToDictionary(kv => kv.Key, kv => kv.Value.Position);
}

record SetLineupRequest(string? Username, int? PeriodIndex, List<string>? ActiveSpotIds);

/// <summary>Shared helpers for the lineup endpoints.</summary>
static class LineupEndpoints
{
    /// <summary>
    /// The requested period, or the current one. "Current" is the last period
    /// whose start has passed — during a week that is the live week, and before
    /// the season it is the first one.
    /// </summary>
    public static async Task<(Period? Period, object[] All)> ResolvePeriodAsync(
        FirestoreDb db, string season, int? index, SimulationClock clock)
    {
        var snap = await db.Collection("periods").WhereEqualTo("season", season).OrderBy("index").GetSnapshotAsync();
        var periods = snap.Documents.Select(d => d.ConvertTo<Period>()).ToList();
        if (periods.Count == 0) return (null, []);

        var all = periods.Select(p => (object)new
        {
            index = p.Index, startDate = p.StartDate, endDate = p.EndDate,
            gameCount = p.GameCount, finalized = p.FinalizedUtc is not null,
        }).ToArray();

        // Eastern game day, not raw UTC — the two disagree for five hours every
        // night, which is long enough to show the wrong week to anyone loading
        // the app late in the evening.
        var today = await clock.TodayEtAsync();
        var chosen = index is not null
            ? periods.FirstOrDefault(p => p.Index == index)
            : periods.LastOrDefault(p => string.CompareOrdinal(p.StartDate, today) <= 0) ?? periods[0];
        return (chosen, all);
    }

    public static object SlotsDto(RuleConfig config) => new
    {
        forwards = config.TopCount.Forwards ?? 0,
        defense = config.TopCount.Defense ?? 0,
        goalies = config.TopCount.Goalies ?? 0,
    };
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
