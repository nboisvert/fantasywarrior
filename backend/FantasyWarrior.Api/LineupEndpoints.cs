using FantasyWarrior.Core.Lineups;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Players;
using FantasyWarrior.Data.Rosters;
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

            var today = PoolClock.TodayEt(now);

            // **The roster of the week being asked about**, not of today. Those
            // used to be the same query, and the difference is the whole bug
            // this screen had: the picker writes next week, so on the days
            // between agreeing a trade and it taking effect it was offering a
            // player who would be gone and hiding the one who would arrive.
            var spots = await db.RosterSpots
                .Where(s => s.TeamId == team.TeamId)
                .Where(RosterWindow.OwnsPeriod(periodDoc.StartDate, periodDoc.EndDate))
                .ToListAsync();

            // Held today but not that week — a man on his way out. Returned so
            // the GM can see where the slot went rather than watch a player
            // vanish, and never selectable: he has no lineup row to set.
            //
            // Only for a week that has not begun. Asking this of a past week
            // would drag in everyone acquired since, who were never leaving
            // anything.
            var leaving = periodDoc.StartDate > today
                ? await db.RosterSpots
                    .Where(s => s.TeamId == team.TeamId && s.EndDate != null && s.EndDate < periodDoc.StartDate)
                    .Where(RosterWindow.OwnedOn(today))
                    .ToListAsync()
                : [];

            var allSpots = spots.Concat(leaving).ToList();
            var playerIds = allSpots.Where(s => s.PlayerId != null).Select(s => s.PlayerId!.Value).ToList();
            var players = await db.Players.Where(p => playerIds.Contains(p.PlayerId))
                .ToDictionaryAsync(p => p.PlayerId);
            var caps = await Queries.CapHitsAsync(db, league.Season, playerIds);
            var franchiseAbbrevs = allSpots.Where(s => s.FranchiseAbbrev != null)
                .Select(s => s.FranchiseAbbrev!).ToList();
            var franchises = await db.NhlTeams.Where(t => franchiseAbbrevs.Contains(t.Abbrev))
                .ToDictionaryAsync(t => t.Abbrev);

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

            // The assignment rows *are* the lineup. This used to compute a
            // preview of what the carry-forward would pick, because next week's
            // rows did not exist until the scoring pass reached it — by which
            // time the week was locked and the preview had never been anyone's
            // choice. WeekAheadJob writes them the night the week before opens,
            // so there is nothing left to guess at.
            var entries = spots.Concat(leaving).Select(s =>
            {
                var franchise = s.FranchiseAbbrev is null ? null : franchises.GetValueOrDefault(s.FranchiseAbbrev);
                var p = s.PlayerId is { } id && players.TryGetValue(id, out var found) ? found : null;
                results.TryGetValue(s.RosterSpotId, out var r);
                var leaves = s.EndDate is { } end && end < periodDoc.StartDate;
                return new
                {
                    spotId = s.RosterSpotId.ToString(),
                    playerId = s.PlayerId,
                    // The franchise's own name, and the logo the Team screen
                    // shows where a player's lineup toggle would be — one
                    // franchise, one seat, so there is nothing to toggle.
                    name = franchise?.Name ?? p?.FullName ?? "Unknown player",
                    position = p?.Position ?? s.PositionGroup,
                    positionGroup = s.PositionGroup,
                    team = franchise?.Abbrev ?? p?.TeamAbbrev,
                    headshotUrl = p?.HeadshotUrl,
                    logoUrl = franchise?.LogoUrl,
                    // A franchise costs no cap and is nobody's contract, which
                    // is the same rule vStandings applies.
                    capHit = s.PlayerId is { } cid && caps.TryGetValue(cid, out var c) ? c : (long?)null,
                    // Mine now, promised away — the "leaving via trade" mark on
                    // a row for a week he still plays.
                    engaged = s.EndDate != null && s.StartDate <= today && s.EndDate >= today,
                    // Not on this week's roster at all: he leaves before it
                    // opens. There is no assignment row to set, so the screen
                    // shows him inert rather than letting a GM pick a man the
                    // PUT would then refuse.
                    leaving = leaves,
                    // Acquired for a week that has not started. He has a real
                    // spot and a real lineup row — he simply is not here today.
                    arriving = s.StartDate > today,
                    active = !leaves && (s.IsFranchise || (r?.IsActive ?? false)),
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
                    teamWins = r?.TeamWins ?? 0,
                    teamLosses = r?.TeamLosses ?? 0,
                    teamOtLosses = r?.TeamOtLosses ?? 0,
                    fromDate = (DateOnly?)r?.EffectiveFrom,
                    toDate = (DateOnly?)r?.EffectiveTo,
                    seasonPoints = seasonPoints.GetValueOrDefault(s.RosterSpotId),
                };
            })
            .OrderBy(e => e.positionGroup switch { "F" => 0, "D" => 1, "G" => 2, _ => 3 })
            .ThenByDescending(e => e.active)
            .ThenByDescending(e => e.seasonPoints)
            .ToList();

            // The Équipe slot is left out on purpose: this counter says how much
            // of his lineup a GM still has to fill, and the franchise is never
            // something he fills. A departing player is out for the same reason
            // — he fills nothing that week, and counting him would tell the GM
            // his lineup is complete when he is a man short.
            var used = new Dictionary<string, int> { ["F"] = 0, ["D"] = 0, ["G"] = 0 };
            foreach (var e in entries.Where(e => e.active && !e.leaving && e.positionGroup != "T"))
                used[e.positionGroup] = used.GetValueOrDefault(e.positionGroup) + 1;

            return Results.Ok(new
            {
                periodIndex = periodDoc.Number, startDate = periodDoc.StartDate, endDate = periodDoc.EndDate,
                gameCount = periodDoc.GameCount, locked, finalized = periodDoc.FinalizedUtc is not null,
                isOwner, hidden = false,
                setBy = lineup?.SetBy,
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
        //
        // The window is the whole season to date (2026-08-04, per Nick), not
        // one week: a free agent worth claiming is one who has produced all
        // year, and a one-week window put a fourth-liner who scored twice on
        // Saturday ahead of a 60-point winger nobody had taken. Bounded to the
        // simulated day when a replay is running, like every other season total.
        app.MapGet("/api/leagues/{leagueId}/free-agents", async (
            string leagueId, int? limit, FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            // No replay running means no bound, expressed as a date no game can
            // fall after — one parameter, one query, rather than two copies of
            // the projection below differing only in a WHERE clause.
            var asOf = (await clock.StateAsync())?.AsOfDate ?? DateOnly.MaxValue;

            // Player spots only. A franchise spot's null PlayerId would land in
            // this list and turn the NOT IN below into `NOT IN (…, NULL)`,
            // which is NULL for every row — an empty free-agent board, with
            // nothing in the logs to say why.
            // Owned once every accepted trade has landed, not owned today: a
            // player arriving somewhere on Monday must not sit on the free-agent
            // board all week, and one leaving on Monday is not free either — his
            // new team already has him.
            var rosteredIds = await db.RosterSpots
                .Where(s => s.LeagueId == league.LeagueId && s.PlayerId != null)
                .Where(RosterWindow.Committed())
                .Select(s => s.PlayerId!.Value)
                .ToListAsync();

            // Summed by the database, unlike the week-long version this
            // replaced: a full season of game lines for every unrostered player
            // is tens of thousands of rows, and not one of them is wanted
            // individually.
            var totals = await db.PlayerGameStats
                .Where(l => l.Season == league.Season && l.GameType == GameType.RegularSeason
                            && l.GameDate <= asOf
                            && !rosteredIds.Contains(l.PlayerId))
                .GroupBy(l => l.PlayerId)
                .Select(g => new SeasonTotals(
                    g.Key,
                    g.Count(),
                    g.Sum(l => l.Goals ?? 0),
                    g.Sum(l => l.Assists ?? 0),
                    g.Sum(l => l.PlusMinus ?? 0),
                    g.Sum(l => l.Pim),
                    g.Sum(l => l.Shots ?? 0),
                    g.Sum(l => l.Hits ?? 0),
                    g.Sum(l => l.BlockedShots ?? 0),
                    g.Count(l => l.Decision == "W"),
                    g.Count(l => l.OtLoss == true),
                    g.Count(l => l.Shutout == true),
                    g.Sum(l => l.GoalsAgainst ?? 0),
                    g.Sum(l => l.Saves ?? 0),
                    g.Sum(l => l.ShotsAgainst ?? 0)))
                .ToListAsync();

            var perPlayer = totals.Select(t => (t.PlayerId, StatColumns.ToStatLine(t)));

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

            // The roster of the week being set, which is what makes a spotId
            // for a player who leaves before it — or has not arrived yet —
            // simply unknown here. LineupRules.Validate then refuses it by
            // name, so a stale screen cannot write a lineup the week itself
            // contradicts.
            var spots = await db.RosterSpots
                .Where(s => s.TeamId == team.TeamId)
                .Where(RosterWindow.OwnsPeriod(periodDoc.StartDate, periodDoc.EndDate))
                .ToListAsync();
            var candidates = spots
                .Select(s => new LineupCandidate(
                    s.RosterSpotId.ToString(), s.PlayerId ?? 0, s.PositionGroup, 0, s.OpenedUtc))
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
                    // The Équipe slot is active whatever the request said. One
                    // franchise, one seat, so there is no decision to make —
                    // and a client that omits it (or predates it) must not be
                    // able to bench one by accident.
                    var shouldBeActive = spot.IsFranchise || activeIds.Contains(spot.RosterSpotId);
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

            // All three of this team's roster tenses, in one read.
            //
            // A departed player is a spot whose stint has *finished* and that
            // was in the lineup for at least one week. Those points are banked
            // to this team permanently — a trade cannot move history — so
            // leaving him out would make the grid disagree with the standings
            // about where the score came from. Spots that never dressed are
            // excluded: bookkeeping, not history.
            //
            // "Finished" is now a date, not a null check. A player traded away
            // on Monday has an end date already, and putting him in Departed
            // while he is still scoring for this team would be a screen
            // contradicting itself.
            var today = await clock.TodayEtAsync();
            var allSpots = await db.RosterSpots
                .Where(s => s.TeamId == team.TeamId)
                .Select(s => new { Spot = s, EverActive = s.Assignments.Any(a => a.IsActive) })
                .ToListAsync();

            var spots = allSpots
                .Where(s => s.Spot.StartDate <= today && (s.Spot.EndDate == null || s.Spot.EndDate >= today))
                .Select(s => s.Spot)
                .ToList();
            var departedSpots = allSpots
                .Where(s => s.Spot.EndDate is { } end && end < today && s.EverActive)
                .Select(s => s.Spot)
                .OrderByDescending(s => s.EndDate)
                .ToList();
            // Acquired in a trade that has not reached its effective date. He
            // has a real spot now, so he goes through the same projection as
            // everyone else — the parallel row shape this used to need, built
            // by player id because no spot existed, is gone.
            var incomingSpots = allSpots
                .Where(s => s.Spot.StartDate > today)
                .Select(s => s.Spot)
                .OrderBy(s => s.StartDate)
                .ToList();

            var playerIds = spots.Concat(departedSpots).Concat(incomingSpots)
                .Where(s => s.PlayerId != null).Select(s => s.PlayerId!.Value)
                .Distinct().ToList();
            var players = await db.Players.Where(p => playerIds.Contains(p.PlayerId))
                .ToDictionaryAsync(p => p.PlayerId);
            var caps = await Queries.CapHitsAsync(db, league.Season, playerIds);

            // The Équipe slots this team has held, open or closed — a franchise
            // traded away belongs in the departed list for the same reason a
            // player does: its banked points are still part of this team's
            // score.
            var franchiseAbbrevs = spots.Concat(departedSpots).Concat(incomingSpots)
                .Where(s => s.FranchiseAbbrev != null).Select(s => s.FranchiseAbbrev!).Distinct().ToList();
            var franchises = await db.NhlTeams.Where(t => franchiseAbbrevs.Contains(t.Abbrev))
                .ToDictionaryAsync(t => t.Abbrev);

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
            var injuries = await Queries.InjuriesAsync(db, playerIds);
            // Never played an NHL game, so the grid sinks him below the roster
            // and keeps him out of the sort entirely. Read here rather than on
            // the client: it is a fact about the player, not about the screen.
            var prospects = await Prospects.ForAsync(db, playerIds);

            // The Équipe slot, as one row of the same grid.
            //
            // Only the three team columns carry a number (Nick, 2026-08-05): a
            // franchise has no goals, no cap hit and no injury, and the screen
            // shows a dash rather than a zero for each. The player-stat fields
            // are still present and still zero — they are the row shape, and
            // the T branch on the grid never reads them.
            object FranchiseRow(RosterSpot spot)
            {
                var f = franchises.GetValueOrDefault(spot.FranchiseAbbrev!);
                spotTotals.TryGetValue(spot.RosterSpotId, out var st);
                return new
                {
                    // Negative, so a franchise row can never collide with an NHL
                    // player id in the grid's row keys. It is a key, not an
                    // identifier anything can be looked up by — clicking a
                    // franchise opens nothing.
                    id = -spot.RosterSpotId,
                    name = f?.Name ?? spot.FranchiseAbbrev!,
                    position = "T",
                    team = spot.FranchiseAbbrev,
                    capHit = (long?)null,
                    headshotUrl = (string?)null,
                    logoUrl = f?.LogoUrl,
                    engaged = false,
                    injuryStatus = (string?)null,
                    injuryType = (string?)null,
                    isGoalie = false,
                    // A franchise is not a player and has no career to have missed.
                    isProspect = false,
                    gamesPlayed = 0, goals = 0, assists = 0, points = 0, plusMinus = 0,
                    pim = 0, shots = 0, hits = 0, blockedShots = 0,
                    wins = 0, otLosses = 0, shutouts = 0,
                    goalsAgainst = 0, saves = 0, shotsAgainst = 0,
                    teamWins = st?.ActiveTeamWins ?? 0,
                    teamLosses = st?.ActiveTeamLosses ?? 0,
                    teamOtLosses = st?.ActiveTeamOtLosses ?? 0,
                    spotStartDate = (DateOnly?)spot.StartDate,
                    spotActiveGamesPlayed = 0,
                    spotActiveGoals = 0,
                    spotActiveAssists = 0,
                    spotActivePoints = st?.ActivePoints ?? 0,
                    spotBenchPoints = 0d,
                    spotEndDate = (DateOnly?)spot.EndDate,
                };
            }

            // One row shape, two lists — the grids are identical by design, so
            // the projection has to be too.
            object Row(RosterSpot spot)
            {
                if (spot.IsFranchise) return FranchiseRow(spot);

                var playerId = spot.PlayerId!.Value;
                players.TryGetValue(playerId, out var p);
                season.TryGetValue(playerId, out var t);
                spotTotals.TryGetValue(spot.RosterSpotId, out var st);
                injuries.TryGetValue(playerId, out var inj);
                return new
                {
                    id = playerId,
                    name = p?.FullName ?? "Unknown player",
                    position = p?.Position ?? spot.PositionGroup,
                    team = p?.TeamAbbrev,
                    capHit = caps.TryGetValue(playerId, out var c) ? c : (long?)null,
                    headshotUrl = p?.HeadshotUrl,
                    logoUrl = (string?)null,
                    // Mine today with a departure already on the books — the
                    // "leaving via trade" mark, and what the trade sheet greys
                    // out instead of letting a GM offer a player twice. Read off
                    // the spot's own dates rather than off the trade's assets:
                    // the spot is where the swap is recorded now, and a second
                    // derivation could disagree with it.
                    engaged = spot.EndDate != null && spot.StartDate <= today && spot.EndDate >= today,
                    // Injured or suspended right now, per the news sources. The
                    // grid marks the row; the type is what the tooltip says.
                    // Deliberately carried on the row rather than fetched
                    // separately: it is one dictionary lookup, and a second
                    // round trip would let the marker arrive after the number
                    // it belongs to.
                    injuryStatus = inj?.Status,
                    injuryType = inj?.InjuryType,
                    isGoalie = spot.PositionGroup == "G",
                    isProspect = prospects.Contains(playerId),
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
                    teamWins = 0, teamLosses = 0, teamOtLosses = 0,
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
                // Acquired in a trade that has not reached its effective date.
                // Through the same projection as everyone else now that he has a
                // real spot: the parallel row shape this needed, with its whole
                // Fantasy-point group hard-coded to zero, is gone.
                incoming = incomingSpots.Select(Row).ToList(),
            });
        });
    }
}

public record SetLineupRequest(string? Username, int? PeriodIndex, List<string>? ActiveSpotIds);
