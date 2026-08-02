using FantasyWarrior.Core.Players;
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

            // The cap hit for the season this card is showing, not the newest
            // one on file. Contracts run years ahead — Jack Eichel is $10M in
            // 2025-26 and $13.5M from 2026-27 under an extension — so taking
            // the latest made the card disagree with the Team grid about the
            // same player, and the grid was right.
            var contract = await db.PlayerContracts
                .Where(c => c.PlayerId == playerId && (season == null || c.Season == season))
                .FirstOrDefaultAsync()
                // No contract for that season (a prospect, a season we hold no
                // deal for): fall back to the earliest on file rather than
                // showing nothing, since it is the nearest true figure.
                ?? await db.PlayerContracts
                    .Where(c => c.PlayerId == playerId)
                    .OrderBy(c => c.Season)
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
                    hits = totals[StatKeys.Hits],
                    blockedShots = totals[StatKeys.BlockedShots],
                    wins = totals[StatKeys.Wins],
                    otLosses = totals[StatKeys.OtLosses],
                    shutouts = totals[StatKeys.Shutouts],
                    goalsAgainst = totals[StatKeys.GoalsAgainst],
                    saves = totals[StatKeys.Saves],
                    shotsAgainst = totals[StatKeys.ShotsAgainst],
                    // Not a scored stat — never rule-configured, so it stays
                    // out of StatKeys/StatLine and is averaged straight from
                    // this season's lines instead.
                    avgToi = TimeOnIce.FormatAverage(lines.Select(l => l.Toi)),
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

        // Career tab: season-by-season history from career-sync's cache, not
        // a live NHL fetch — this is a read-only DB query like every other
        // endpoint here. Most recent season first, matching how a career
        // stats page is normally read.
        app.MapGet("/api/players/{playerId:long}/career", async (long playerId, FantasyWarriorDbContext db) =>
        {
            var rows = await db.PlayerCareerSeasonStats
                .AsNoTracking()
                .Where(s => s.PlayerId == playerId)
                .OrderByDescending(s => s.Season)
                .Select(s => new
                {
                    season = s.Season,
                    league = s.LeagueAbbrev,
                    team = s.TeamName,
                    gamesPlayed = s.GamesPlayed,
                    goals = s.Goals,
                    assists = s.Assists,
                    points = s.Points,
                    pim = s.Pim,
                    plusMinus = s.PlusMinus,
                    wins = s.Wins,
                    losses = s.Losses,
                    otLosses = s.OtLosses,
                    goalsAgainst = s.GoalsAgainst,
                    goalsAgainstAvg = s.GoalsAgainstAvg,
                    savePctg = s.SavePctg,
                    shutouts = s.Shutouts,
                })
                .ToListAsync();

            return Results.Ok(rows);
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
