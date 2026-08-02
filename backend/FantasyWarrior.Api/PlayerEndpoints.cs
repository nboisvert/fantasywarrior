using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

public static class PlayerEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/players", async (string? q, FantasyWarriorDbContext db) =>
        {
            var term = (q ?? "").Trim();
            var query = db.Players.AsNoTracking();
            if (term.Length > 0)
                query = query.Where(p => p.FirstName.Contains(term) || p.LastName.Contains(term));

            var rows = await query
                .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
                .Take(20)
                .Select(p => new
                {
                    id = p.PlayerId,
                    name = p.FirstName + " " + p.LastName,
                    position = p.Position,
                    team = p.TeamAbbrev,
                    status = p.Status,
                    capHit = (long?)null,
                    p.HeadshotUrl,
                })
                .ToListAsync();

            return Results.Ok(rows.Select(r => new
            {
                r.id, r.name, r.position, r.team, r.status, r.capHit, headshotUrl = r.HeadshotUrl,
            }));
        });

        app.MapGet("/api/players/{playerId:long}", async (
            long playerId, FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.PlayerId == playerId);
            if (player is null) return Results.NotFound(new { error = "Player not found." });

            // During a season replay the card must show what was known on the
            // simulated day — otherwise a November card reports April totals and
            // lists games that have not been played yet.
            //
            // Under Firestore this needed a cache with a throughDate and its own
            // invalidation rules. Here it is a WHERE clause.
            var asOf = (await clock.StateAsync())?.AsOfDate;

            var season = await db.PlayerGameStats
                .Where(l => l.PlayerId == playerId)
                .OrderByDescending(l => l.Season)
                .Select(l => l.Season)
                .FirstOrDefaultAsync();

            var lines = season is null
                ? []
                : await db.PlayerGameStats
                    .Where(l => l.PlayerId == playerId && l.Season == season
                                && l.GameType == GameType.RegularSeason
                                && (asOf == null || l.GameDate <= asOf))
                    .OrderByDescending(l => l.GameDate)
                    .ToListAsync();

            var totals = StatLine.Sum(lines.Select(StatColumns.ToStatLine));
            var contract = await db.PlayerContracts
                .Where(c => c.PlayerId == playerId)
                .OrderByDescending(c => c.Season)
                .FirstOrDefaultAsync();

            return Results.Ok(new
            {
                id = player.PlayerId,
                name = player.FullName,
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
                capHit = contract?.CapHit,
                draftYear = player.DraftYear,
                draftRound = player.DraftRound,
                draftOverall = player.DraftOverall,
                draftTeamAbbrev = player.DraftTeamAbbrev,
                isGoalie = player.Position == "G",
                season,
                seasonTotals = new
                {
                    gamesPlayed = totals[StatKeys.GamesPlayed],
                    goals = totals[StatKeys.Goals],
                    assists = totals[StatKeys.Assists],
                    points = totals[StatKeys.Goals] + totals[StatKeys.Assists],
                    plusMinus = totals[StatKeys.PlusMinus],
                    pim = totals[StatKeys.Pim],
                    shots = totals[StatKeys.Shots],
                    wins = totals[StatKeys.Wins],
                    otLosses = totals[StatKeys.OtLosses],
                    shutouts = totals[StatKeys.Shutouts],
                    goalsAgainst = totals[StatKeys.GoalsAgainst],
                    saves = totals[StatKeys.Saves],
                    shotsAgainst = totals[StatKeys.ShotsAgainst],
                },
                recentGames = lines.Take(10).Select(l => new
                {
                    date = l.GameDate,
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

        app.MapGet("/api/news", async (int? limit, FantasyWarriorDbContext db) =>
        {
            var take = Math.Clamp(limit ?? 30, 1, 50);
            var rows = await db.NewsItems
                .AsNoTracking()
                .OrderByDescending(n => n.PublishedUtc)
                .Take(take)
                .Select(n => new
                {
                    id = n.NewsItemId.ToString(),
                    source = n.Source,
                    headline = n.Headline,
                    url = n.Url,
                    playerId = n.PlayerId,
                    playerName = n.PlayerName,
                    publishedUtc = n.PublishedUtc,
                })
                .ToListAsync();
            return Results.Ok(rows);
        });

        // --- roster add/drop ---------------------------------------------
        // Effective today rather than at a week boundary, unlike a trade: a free
        // agent pickup is meant to be immediate, and StatWindow already handles
        // a spot that opens mid-week by giving it only the days it owned.

        app.MapPost("/api/leagues/{leagueId}/teams/{username}/roster", async (
            string leagueId, string username, RosterChangeRequest req,
            FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var team = await Queries.TeamAsync(db, league.LeagueId, Queries.Normalize(username));
            if (team is null) return Results.NotFound(new { error = "Team not found." });

            var player = await db.Players.FirstOrDefaultAsync(p => p.PlayerId == req.PlayerId);
            if (player is null) return Results.BadRequest(new { error = "Unknown player id." });

            // One owner per player per league is a unique index now, so this
            // check is a friendly message rather than the enforcement itself —
            // a race that got past it would fail at the insert, not corrupt.
            var taken = await db.RosterSpots
                .Where(s => s.LeagueId == league.LeagueId && s.PlayerId == req.PlayerId && s.EndDate == null)
                .Select(s => new { s.TeamId, TeamName = s.Team!.Name })
                .FirstOrDefaultAsync();
            if (taken is not null)
                return taken.TeamId == team.TeamId
                    ? Results.BadRequest(new { error = "Player already on this roster." })
                    : Results.Conflict(new { error = $"{player.FullName} is already on team '{taken.TeamName}'." });

            db.RosterSpots.Add(new RosterSpot
            {
                LeagueId = league.LeagueId,
                TeamId = team.TeamId,
                PlayerId = req.PlayerId,
                PositionGroup = PositionGroups.CodeFrom(player.Position),
                StartDate = PoolClock.TodayEt(await clock.NowAsync()),
                StartReason = RosterSpotStartReason.FreeAgent,
                OpenedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return Results.Ok(Dtos.Player(player));
        });

        app.MapDelete("/api/leagues/{leagueId}/teams/{username}/roster/{playerId:long}", async (
            string leagueId, string username, long playerId,
            FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var team = await Queries.TeamAsync(db, league.LeagueId, Queries.Normalize(username));
            if (team is null) return Results.NotFound(new { error = "Team not found." });

            var spot = await db.RosterSpots
                .FirstOrDefaultAsync(s => s.TeamId == team.TeamId && s.PlayerId == playerId && s.EndDate == null);
            if (spot is null) return Results.BadRequest(new { error = "Player is not on this roster." });

            // Closed, never deleted: the team keeps whatever this player banked
            // for it, permanently.
            spot.EndDate = PoolClock.TodayEt(await clock.NowAsync());
            spot.EndReason = RosterSpotEndReason.Release;
            spot.ClosedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }
}

public record RosterChangeRequest(long PlayerId, string? CreationEvent, string? CreationEventReferenceId);
