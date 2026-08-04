using FantasyWarrior.Core.Lineups;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>
/// The weekly lineup — the one place in the app where a rule is actually
/// enforced rather than merely displayed. Roster size and the salary cap are
/// still advisory.
/// </summary>
public static class LineupEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/leagues/{leagueId}/teams/{username}/lineup", async (
            string leagueId, string username, int? period, string? viewer,
            FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var owner = Queries.Normalize(username);
            var team = await Queries.TeamAsync(db, league.LeagueId, owner);
            if (team is null) return Results.NotFound(new { error = "Team not found." });

            var now = await clock.NowAsync();
            var (periodDoc, allPeriods) = await Queries.ResolvePeriodAsync(
                db, league.Season, period, PoolClock.TodayEt(now));
            if (periodDoc is null) return Results.NotFound(new { error = "No period calendar for this season." });

            var locked = periodDoc.LockUtc <= now.UtcDateTime;
            var slots = new { forwards = league.ActiveForwards, defense = league.ActiveDefense, goalies = league.ActiveGoalies };
            var periodsDto = allPeriods.Select(p => Dtos.Period(p, now)).ToArray();

            // A rival's lineup is competitive information until it locks.
            var isOwner = viewer is not null && Queries.Normalize(viewer) == owner;
            if (!isOwner && !locked)
                return Results.Ok(new
                {
                    periodIndex = periodDoc.Number, startDate = periodDoc.StartDate, endDate = periodDoc.EndDate,
                    gameCount = periodDoc.GameCount, locked, finalized = periodDoc.FinalizedUtc is not null,
                    isOwner = false, hidden = true,
                    slots, used = new { }, entries = Array.Empty<object>(), periods = periodsDto,
                });

            var spots = await db.RosterSpots
                .Where(s => s.TeamId == team.TeamId && s.EndDate == null)
                .ToListAsync();
            var playerIds = spots.Select(s => s.PlayerId).ToList();
            var players = await db.Players.Where(p => playerIds.Contains(p.PlayerId))
                .ToDictionaryAsync(p => p.PlayerId);
            var caps = await Queries.CapHitsAsync(db, league.Season, playerIds);

            var results = await db.RosterAssignments
                .Where(a => a.PeriodId == periodDoc.PeriodId && spots.Select(s => s.RosterSpotId).Contains(a.RosterSpotId))
                .ToDictionaryAsync(a => a.RosterSpotId);
            var seasonPoints = await db.RosterSpotTotals
                .Where(v => v.TeamId == team.TeamId)
                .ToDictionaryAsync(v => v.RosterSpotId, v => v.ActivePoints);
            var lineup = await db.TeamPeriodLineups
                .FirstOrDefaultAsync(l => l.TeamId == team.TeamId && l.PeriodId == periodDoc.PeriodId);

            var weekTotals = await db.TeamPeriodScores
                .FirstOrDefaultAsync(v => v.TeamId == team.TeamId && v.PeriodId == periodDoc.PeriodId);

            // A week the nightly job has not reached yet has no assignment rows
            // at all, so every player would read as benched — a GM setting next
            // week's lineup ahead of time would start from an empty team rather
            // than from the one he is already fielding.
            //
            // Show him what the job *would* pick: last week's active set carried
            // forward and topped up. Computed, never written: this is a preview
            // of a default, and persisting it would turn "we picked this for
            // you" into "you chose this", which is the distinction the whole
            // forgotten-lineup rule rests on.
            var previewed = new HashSet<int>();
            if (results.Count == 0)
            {
                var previous = await db.Periods
                    .Where(p => p.Season == league.Season && p.Number < periodDoc.Number)
                    .OrderByDescending(p => p.Number)
                    .FirstOrDefaultAsync();
                var previousActive = previous is null
                    ? []
                    : (await db.RosterAssignments
                        .Where(a => a.PeriodId == previous.PeriodId && a.IsActive
                                    && spots.Select(s => s.RosterSpotId).Contains(a.RosterSpotId))
                        .Select(a => a.RosterSpotId)
                        .ToListAsync());

                var seasonSoFar = await db.RosterSpotTotals
                    .Where(v => v.TeamId == team.TeamId)
                    .ToDictionaryAsync(v => v.RosterSpotId, v => v.ActivePoints);
                var candidates = spots
                    .Select(s => new LineupCandidate(
                        s.RosterSpotId.ToString(), s.PlayerId, s.PositionGroup,
                        seasonSoFar.GetValueOrDefault(s.RosterSpotId), s.OpenedUtc))
                    .ToList();

                foreach (var id in LineupRules.CarryForward(
                             candidates,
                             [.. previousActive.Select(id => id.ToString())],
                             new LineupSlots(league.ActiveForwards, league.ActiveDefense, league.ActiveGoalies)))
                    previewed.Add(int.Parse(id));
            }

            var entries = spots.Select(s =>
            {
                players.TryGetValue(s.PlayerId, out var p);
                results.TryGetValue(s.RosterSpotId, out var r);
                return new
                {
                    spotId = s.RosterSpotId.ToString(),
                    playerId = s.PlayerId,
                    name = p?.FullName ?? "Unknown player",
                    position = p?.Position ?? s.PositionGroup,
                    positionGroup = s.PositionGroup,
                    team = p?.TeamAbbrev,
                    headshotUrl = p?.HeadshotUrl,
                    capHit = caps.TryGetValue(s.PlayerId, out var c) ? c : (long?)null,
                    active = r?.IsActive ?? previewed.Contains(s.RosterSpotId),
                    points = r?.FantasyPoints ?? 0,
                    gamesPlayed = r?.GamesPlayed ?? 0,
                    // The week's raw line, so a card can say what actually
                    // happened rather than only what it scored. Already on the
                    // assignment row, so this costs nothing extra to return.
                    goals = r?.Goals ?? 0,
                    assists = r?.Assists ?? 0,
                    wins = r?.Wins ?? 0,
                    otLosses = r?.OtLosses ?? 0,
                    saves = r?.Saves ?? 0,
                    fromDate = (DateOnly?)r?.EffectiveFrom,
                    toDate = (DateOnly?)r?.EffectiveTo,
                    seasonPoints = seasonPoints.GetValueOrDefault(s.RosterSpotId),
                };
            })
            .OrderBy(e => e.positionGroup == "F" ? 0 : e.positionGroup == "D" ? 1 : 2)
            .ThenByDescending(e => e.active)
            .ThenByDescending(e => e.seasonPoints)
            .ToList();

            var used = new Dictionary<string, int> { ["F"] = 0, ["D"] = 0, ["G"] = 0 };
            foreach (var e in entries.Where(e => e.active))
                used[e.positionGroup] = used.GetValueOrDefault(e.positionGroup) + 1;

            return Results.Ok(new
            {
                periodIndex = periodDoc.Number, startDate = periodDoc.StartDate, endDate = periodDoc.EndDate,
                gameCount = periodDoc.GameCount, locked, finalized = periodDoc.FinalizedUtc is not null,
                isOwner, hidden = false,
                setBy = lineup?.SetBy ?? (previewed.Count > 0 ? LineupSetBy.Auto : null),
                submittedUtc = lineup?.SubmittedUtc,
                activePoints = weekTotals?.ActivePoints ?? 0,
                benchPoints = weekTotals?.BenchPoints ?? 0,
                slots, used, entries, periods = periodsDto,
            });
        });

        // League-wide unrostered players, ranked by fantasy points under the
        // league's own scale rather than raw NHL points — the only way a
        // goalie's wins/saves can compete with a skater's goals/assists for a
        // spot on this list.
        app.MapGet("/api/leagues/{leagueId}/free-agents", async (
            string leagueId, int? period, int? limit, FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var now = await clock.NowAsync();
            var (periodDoc, _) = await Queries.ResolvePeriodAsync(
                db, league.Season, period, PoolClock.TodayEt(now));
            if (periodDoc is null) return Results.NotFound(new { error = "No period calendar for this season." });

            var rosteredIds = await db.RosterSpots
                .Where(s => s.LeagueId == league.LeagueId && s.EndDate == null)
                .Select(s => s.PlayerId)
                .ToListAsync();

            var lines = await db.PlayerGameStats
                .Where(l => l.Season == league.Season && l.GameType == GameType.RegularSeason
                            && l.GameDate >= periodDoc.StartDate && l.GameDate <= periodDoc.EndDate
                            && !rosteredIds.Contains(l.PlayerId))
                .ToListAsync();

            var perPlayer = lines
                .GroupBy(l => l.PlayerId)
                .Select(g => (g.Key, StatLine.Sum(g.Select(StatColumns.ToStatLine))));

            var scale = await Queries.ScaleAsync(db, league.LeagueId);
            var ranked = FreeAgentRanking.Rank(perPlayer, scale, limit ?? 4);

            var playerIds = ranked.Select(r => r.PlayerId).ToList();
            var players = await db.Players.Where(p => playerIds.Contains(p.PlayerId))
                .ToDictionaryAsync(p => p.PlayerId);

            return Results.Ok(ranked.Select(r =>
            {
                players.TryGetValue(r.PlayerId, out var p);
                return new
                {
                    playerId = r.PlayerId,
                    name = p?.FullName ?? "Unknown player",
                    position = p?.Position ?? "F",
                    positionGroup = p?.PositionGroup ?? "F",
                    team = p?.TeamAbbrev,
                    headshotUrl = p?.HeadshotUrl,
                    points = r.Points,
                    gamesPlayed = r.Line[StatKeys.GamesPlayed],
                    goals = r.Line[StatKeys.Goals],
                    assists = r.Line[StatKeys.Assists],
                    wins = r.Line[StatKeys.Wins],
                    otLosses = r.Line[StatKeys.OtLosses],
                    shutouts = r.Line[StatKeys.Shutouts],
                    goalsAgainst = r.Line[StatKeys.GoalsAgainst],
                    saves = r.Line[StatKeys.Saves],
                    shotsAgainst = r.Line[StatKeys.ShotsAgainst],
                };
            }));
        });

        app.MapMethods("/api/leagues/{leagueId}/teams/{username}/lineup", ["PUT"], async (
            string leagueId, string username, SetLineupRequest req,
            FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { error = "Username is required." });

            var owner = Queries.Normalize(username);
            // No real auth yet, so this only stops accidents, not attacks.
            // Silently benching a rival's best player every Sunday would be
            // undetectable — this endpoint wants a token check before real users
            // touch it.
            if (Queries.Normalize(req.Username) != owner)
                return Results.Json(new { error = "You can only set your own lineup." }, statusCode: 403);

            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var team = await Queries.TeamAsync(db, league.LeagueId, owner);
            if (team is null) return Results.NotFound(new { error = "Team not found." });

            var now = await clock.NowAsync();
            var (periodDoc, _) = await Queries.ResolvePeriodAsync(
                db, league.Season, req.PeriodIndex, PoolClock.TodayEt(now));
            if (periodDoc is null) return Results.NotFound(new { error = "Period not found." });

            if (periodDoc.LockUtc <= now.UtcDateTime)
                return Results.Conflict(new
                {
                    error = $"Week {periodDoc.Number} is locked. Set your lineup for the next week instead.",
                });

            var spots = await db.RosterSpots
                .Where(s => s.TeamId == team.TeamId && s.EndDate == null)
                .ToListAsync();
            var candidates = spots
                .Select(s => new LineupCandidate(
                    s.RosterSpotId.ToString(), s.PlayerId, s.PositionGroup, 0, s.OpenedUtc))
                .ToList();
            var requested = (req.ActiveSpotIds ?? []).Distinct().ToList();

            var errors = LineupRules.Validate(
                candidates, requested,
                new LineupSlots(league.ActiveForwards, league.ActiveDefense, league.ActiveGoalies));
            if (errors.Count > 0) return Results.BadRequest(new { error = string.Join(" ", errors), errors });

            var activeIds = requested.Select(int.Parse).ToHashSet();

            // **This is what makes a submitted lineup distinguishable from an
            // auto-filled one**, and the scoring pass depends on it: the IsActive
            // flags say who plays, and the TeamPeriodLineup row attributed to the
            // GM stops the job from overwriting his choices with its own.
            //
            // One transaction, so two tabs racing cannot leave half a lineup.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();

                var existing = await db.RosterAssignments
                    .Where(a => a.PeriodId == periodDoc.PeriodId
                                && spots.Select(s => s.RosterSpotId).Contains(a.RosterSpotId))
                    .ToDictionaryAsync(a => a.RosterSpotId);

                foreach (var spot in spots)
                {
                    var shouldBeActive = activeIds.Contains(spot.RosterSpotId);
                    if (existing.TryGetValue(spot.RosterSpotId, out var row))
                    {
                        // A banked week is immutable; the lock above normally
                        // prevents reaching one, but the rule belongs here too.
                        if (!row.IsFinalized) row.IsActive = shouldBeActive;
                    }
                    else
                    {
                        db.RosterAssignments.Add(new RosterAssignment
                        {
                            RosterSpotId = spot.RosterSpotId,
                            PeriodId = periodDoc.PeriodId,
                            IsActive = shouldBeActive,
                            EffectiveFrom = spot.StartDate > periodDoc.StartDate ? spot.StartDate : periodDoc.StartDate,
                            EffectiveTo = periodDoc.EndDate,
                            ScoredUtc = DateTime.UtcNow,
                        });
                    }
                }

                var lineup = await db.TeamPeriodLineups
                    .FirstOrDefaultAsync(l => l.TeamId == team.TeamId && l.PeriodId == periodDoc.PeriodId);
                if (lineup is null)
                    db.TeamPeriodLineups.Add(new TeamPeriodLineup
                    {
                        TeamId = team.TeamId, PeriodId = periodDoc.PeriodId,
                        SetBy = owner, SubmittedUtc = DateTime.UtcNow,
                    });
                else
                {
                    lineup.SetBy = owner;
                    lineup.SubmittedUtc = DateTime.UtcNow;
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
            });

            return Results.Ok(new { ok = true, periodIndex = periodDoc.Number, active = requested.Count });
        });

        // One player's season, week by week, for this team.
        //
        // This is the shape the whole schema was built around: one
        // RosterAssignment row per (roster spot, period), carrying the stats,
        // the points and whether the GM had him active. Answering it is a
        // single indexed read — the document model this replaced would have
        // needed one read per week per player, which is why the question was
        // never asked.
        //
        // Includes closed spots: a player traded away and re-acquired has two
        // stints, and both belong to this team's history of him.
        app.MapGet("/api/leagues/{leagueId}/teams/{username}/players/{playerId:long}/periods", async (
            string leagueId, string username, long playerId, FantasyWarriorDbContext db) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var team = await Queries.TeamAsync(db, league.LeagueId, Queries.Normalize(username));
            if (team is null) return Results.NotFound(new { error = "Team not found." });

            var rows = await db.RosterAssignments
                .Where(a => a.RosterSpot!.TeamId == team.TeamId && a.RosterSpot.PlayerId == playerId)
                .OrderBy(a => a.Period!.Number)
                .Select(a => new
                {
                    periodIndex = a.Period!.Number,
                    startDate = a.Period.StartDate,
                    endDate = a.Period.EndDate,
                    gameCount = a.Period.GameCount,
                    finalized = a.Period.FinalizedUtc != null,
                    active = a.IsActive,
                    points = a.FantasyPoints,
                    a.GamesPlayed,
                    a.Goals,
                    a.Assists,
                    a.PlusMinus,
                    a.Pim,
                    a.Shots,
                    a.Hits,
                    a.BlockedShots,
                    a.Wins,
                    a.OtLosses,
                    a.Shutouts,
                    a.Saves,
                    a.GoalsAgainst,
                    a.ShotsAgainst,
                    // The days this spot actually owned. Usually the whole week;
                    // not when he arrived or left part-way through one.
                    from = a.EffectiveFrom,
                    to = a.EffectiveTo,
                })
                .ToListAsync();

            return Results.Ok(new
            {
                playerId,
                periods = rows,
                // Totals over the same rows, so the panel's footer can never
                // disagree with what it is summing.
                totals = new
                {
                    activePoints = rows.Where(r => r.active).Sum(r => r.points),
                    benchPoints = rows.Where(r => !r.active).Sum(r => r.points),
                    activeWeeks = rows.Count(r => r.active),
                    benchedWeeks = rows.Count(r => !r.active),
                    gamesPlayed = rows.Where(r => r.active).Sum(r => r.GamesPlayed),
                },
            });
        });

        app.MapGet("/api/leagues/{leagueId}/teams/{username}/season-stats", async (
            string leagueId, string username, FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var owner = Queries.Normalize(username);
            var team = await Queries.TeamAsync(db, league.LeagueId, owner);
            if (team is null) return Results.NotFound(new { error = "Team not found." });

            // Both halves of this team's roster history, in one read.
            //
            // A departed player is a *closed* spot that was in the lineup for at
            // least one week. Those points are banked to this team permanently —
            // a trade cannot move history — so leaving him out would make the
            // grid disagree with the standings about where the score came from.
            // Closed spots that never dressed are excluded: bookkeeping, not
            // history.
            var allSpots = await db.RosterSpots
                .Where(s => s.TeamId == team.TeamId)
                .Select(s => new { Spot = s, EverActive = s.Assignments.Any(a => a.IsActive) })
                .ToListAsync();

            var spots = allSpots.Where(s => s.Spot.EndDate == null).Select(s => s.Spot).ToList();
            var departedSpots = allSpots
                .Where(s => s.Spot.EndDate != null && s.EverActive)
                .Select(s => s.Spot)
                .OrderByDescending(s => s.EndDate)
                .ToList();

            var playerIds = spots.Concat(departedSpots).Select(s => s.PlayerId).Distinct().ToList();
            var players = await db.Players.Where(p => playerIds.Contains(p.PlayerId))
                .ToDictionaryAsync(p => p.PlayerId);
            var caps = await Queries.CapHitsAsync(db, league.Season, playerIds);

            // Season totals, bounded to the simulated day when a replay is
            // running — the same bound the player card uses, so the two cannot
            // disagree about the same player on the same screen.
            //
            // No cache, no throughDate field, no invalidation rules. This is the
            // aggregate the Firestore build kept a whole collection for.
            var season = await Queries.SeasonTotalsAsync(
                db, league.Season, playerIds, (await clock.StateAsync())?.AsOfDate);
            var spotTotals = await db.RosterSpotTotals
                .Where(v => v.TeamId == team.TeamId)
                .ToDictionaryAsync(v => v.RosterSpotId);
            var engagedPlayers = (await TradeValidation.EngagedAssetsAsync(db, league.LeagueId)).PlayerIds;
            var injuries = await Queries.InjuriesAsync(db, playerIds);

            // One row shape, two lists — the grids are identical by design, so
            // the projection has to be too.
            object Row(RosterSpot spot)
            {
                players.TryGetValue(spot.PlayerId, out var p);
                season.TryGetValue(spot.PlayerId, out var t);
                spotTotals.TryGetValue(spot.RosterSpotId, out var st);
                injuries.TryGetValue(spot.PlayerId, out var inj);
                return new
                {
                    id = spot.PlayerId,
                    name = p?.FullName ?? "Unknown player",
                    position = p?.Position ?? spot.PositionGroup,
                    team = p?.TeamAbbrev,
                    capHit = caps.TryGetValue(spot.PlayerId, out var c) ? c : (long?)null,
                    headshotUrl = p?.HeadshotUrl,
                    // Already moving in an accepted trade — the trade sheet
                    // greys these out instead of letting a GM build an offer
                    // the server will refuse.
                    engaged = engagedPlayers.Contains(spot.PlayerId),
                    // Injured or suspended right now, per the news sources. The
                    // grid marks the row; the type is what the tooltip says.
                    // Deliberately carried on the row rather than fetched
                    // separately: it is one dictionary lookup, and a second
                    // round trip would let the marker arrive after the number
                    // it belongs to.
                    injuryStatus = inj?.Status,
                    injuryType = inj?.InjuryType,
                    isGoalie = spot.PositionGroup == "G",
                    gamesPlayed = t?.GamesPlayed ?? 0,
                    goals = t?.Goals ?? 0,
                    assists = t?.Assists ?? 0,
                    points = (t?.Goals ?? 0) + (t?.Assists ?? 0),
                    plusMinus = t?.PlusMinus ?? 0,
                    pim = t?.Pim ?? 0,
                    shots = t?.Shots ?? 0,
                    hits = t?.Hits ?? 0,
                    blockedShots = t?.BlockedShots ?? 0,
                    wins = t?.Wins ?? 0,
                    otLosses = t?.OtLosses ?? 0,
                    shutouts = t?.Shutouts ?? 0,
                    goalsAgainst = t?.GoalsAgainst ?? 0,
                    saves = t?.Saves ?? 0,
                    shotsAgainst = t?.ShotsAgainst ?? 0,
                    spotStartDate = (DateOnly?)spot.StartDate,
                    spotActiveGamesPlayed = st?.ActiveGamesPlayed ?? 0,
                    spotActiveGoals = st?.ActiveGoals ?? 0,
                    spotActiveAssists = st?.ActiveAssists ?? 0,
                    spotActivePoints = st?.ActivePoints ?? 0,
                    spotBenchPoints = st?.BenchPoints ?? 0,
                    // Null while he is still held, the day he left otherwise.
                    // The only field that tells the two lists apart.
                    spotEndDate = (DateOnly?)spot.EndDate,
                };
            }

            return Results.Ok(new
            {
                season = league.Season,
                players = spots.Select(Row).ToList(),
                // A separate list rather than a flag on the first: these players
                // answer a different question — where did the score come from —
                // and the screen shows them apart.
                departed = departedSpots.Select(Row).ToList(),
            });
        });
    }
}

public record SetLineupRequest(string? Username, int? PeriodIndex, List<string>? ActiveSpotIds);
