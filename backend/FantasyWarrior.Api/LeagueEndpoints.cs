using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

public static class LeagueEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/login", async (LoginRequest req, FantasyWarriorDbContext db) =>
        {
            var display = req.Username?.Trim() ?? "";
            if (display.Length is < 2 or > 30)
                return Results.BadRequest(new { error = "Username must be 2-30 characters." });

            var id = Queries.Normalize(display);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == id);
            var now = DateTime.UtcNow;
            if (user is null)
            {
                user = new User { Username = id, DisplayName = display, CreatedUtc = now, LastLoginUtc = now };
                db.Users.Add(user);
            }
            else
            {
                user.LastLoginUtc = now;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { username = user.Username, displayName = user.DisplayName });
        });

        app.MapGet("/api/users/{username}/leagues", async (string username, FantasyWarriorDbContext db) =>
        {
            var normalized = Queries.Normalize(username);
            var rows = await db.LeagueMembers
                .Where(m => m.User!.Username == normalized)
                .Select(m => new
                {
                    id = m.League!.JoinCode,
                    m.League.Name,
                    m.League.Season,
                    m.League.CapAmount,
                    members = m.League.Members.Count,
                })
                .ToListAsync();
            return Results.Ok(rows);
        });

        app.MapPost("/api/leagues", async (CreateLeagueRequest req, FantasyWarriorDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { error = "Name and username are required." });

            var username = Queries.Normalize(req.Username);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null)
            {
                user = new User { Username = username, DisplayName = req.Username.Trim(), CreatedUtc = DateTime.UtcNow };
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            var now = DateTime.UtcNow;
            var league = new League
            {
                Name = req.Name.Trim(),
                Season = req.Season ?? "20262027",
                JoinCode = await UniqueCodeAsync(db),
                CommissionerUserId = user.UserId,
                CapAmount = req.CapAmount,
                // Defaults matching what a new Firestore league used to get.
                ActiveForwards = 0, ActiveDefense = 0, ActiveGoalies = 0,
                CreatedUtc = now,
            };
            db.Leagues.Add(league);
            await db.SaveChangesAsync();

            foreach (var (key, value) in new (string, double)[]
                     {
                         (StatKeys.Goals, 1), (StatKeys.Assists, 1),
                         (StatKeys.Wins, 2), (StatKeys.OtLosses, 1), (StatKeys.Shutouts, 0),
                     })
                db.LeagueScoringRules.Add(new LeagueScoringRule
                {
                    LeagueId = league.LeagueId, StatKey = key, PointValue = value,
                });

            db.LeagueMembers.Add(new LeagueMember { LeagueId = league.LeagueId, UserId = user.UserId, JoinedUtc = now });
            db.Teams.Add(new Team
            {
                LeagueId = league.LeagueId,
                OwnerUserId = user.UserId,
                Name = string.IsNullOrWhiteSpace(req.TeamName) ? $"Team {req.Username.Trim()}" : req.TeamName.Trim(),
                CreatedUtc = now,
            });
            await db.SaveChangesAsync();

            return Results.Ok(new { id = league.JoinCode });
        });

        app.MapPost("/api/leagues/{leagueId}/join", async (
            string leagueId, JoinLeagueRequest req, FantasyWarriorDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { error = "Username is required." });

            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var username = Queries.Normalize(req.Username);
            var now = DateTime.UtcNow;
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null)
            {
                user = new User { Username = username, DisplayName = req.Username.Trim(), CreatedUtc = now };
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            if (!await db.LeagueMembers.AnyAsync(m => m.LeagueId == league.LeagueId && m.UserId == user.UserId))
                db.LeagueMembers.Add(new LeagueMember { LeagueId = league.LeagueId, UserId = user.UserId, JoinedUtc = now });

            if (!await db.Teams.AnyAsync(t => t.LeagueId == league.LeagueId && t.OwnerUserId == user.UserId))
                db.Teams.Add(new Team
                {
                    LeagueId = league.LeagueId,
                    OwnerUserId = user.UserId,
                    Name = string.IsNullOrWhiteSpace(req.TeamName) ? $"Team {req.Username.Trim()}" : req.TeamName.Trim(),
                    CreatedUtc = now,
                });

            await db.SaveChangesAsync();
            return Results.Ok(new { id = league.JoinCode });
        });

        app.MapMethods("/api/leagues/{leagueId}/rules", ["PATCH"], async (
            string leagueId, UpdateRulesRequest req, FantasyWarriorDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || req.RuleConfig is null)
                return Results.BadRequest(new { error = "Username and ruleConfig are required." });

            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var commissioner = await db.Users.FindAsync(league.CommissionerUserId);
            if (commissioner?.Username != Queries.Normalize(req.Username))
                return Results.Json(new { error = "Only the commissioner can change the rules." }, statusCode: 403);

            var config = req.RuleConfig;
            // Was a hand-copied reimplementation of this until 2026-08-03. Two
            // identical copies of a validation rule is exactly how they stop
            // being identical.
            var errors = RuleConfigValidation.Validate(config);
            if (errors.Count > 0)
                return Results.BadRequest(new { error = string.Join(" ", errors), errors });

            league.ActiveForwards = config.TopCount.Forwards ?? 0;
            league.ActiveDefense = config.TopCount.Defense ?? 0;
            league.ActiveGoalies = config.TopCount.Goalies ?? 0;
            league.RosterMin = config.RosterSize.Min;
            league.RosterMax = config.RosterSize.Max;
            league.DraftRounds = config.DraftRounds;

            // Replace the whole scale: a value removed from the config must stop
            // scoring, which an upsert-only pass would never achieve.
            await db.LeagueScoringRules.Where(r => r.LeagueId == league.LeagueId).ExecuteDeleteAsync();
            foreach (var (key, value) in config.ScoringScale())
                db.LeagueScoringRules.Add(new LeagueScoringRule
                {
                    LeagueId = league.LeagueId, StatKey = key, PointValue = value,
                });
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                ok = true,
                note = "Applies from the next nightly scoring run. Weeks already banked keep the "
                     + "scale they were scored under — run `recompute` to restate them.",
            });
        });

        // The league screen's one read. Standings come from a view, so this is a
        // handful of queries regardless of how many teams there are — the
        // Firestore version had to precompute a dozen fields nightly to avoid an
        // N+1 it could not express.
        app.MapGet("/api/leagues/{leagueId}", async (
            string leagueId, string? username, FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var now = await clock.NowAsync();
            var today = PoolClock.TodayEt(now);
            var (currentPeriod, _) = await Queries.ResolvePeriodAsync(db, league.Season, null, today);

            var standings = await db.Standings
                .Where(s => s.LeagueId == league.LeagueId)
                .ToListAsync();
            var teams = await db.Teams
                .Where(t => t.LeagueId == league.LeagueId)
                .Select(t => new { t.TeamId, t.Name, t.FranchiseAbbrev, Owner = t.Owner!.Username })
                .ToListAsync();
            var byTeam = standings.ToDictionary(s => s.TeamId);

            // This week's contribution, per team — the "+N this week" the
            // standings show. LivePoints on the view is everything not yet
            // banked, which is that week and nothing else.
            var currentWeek = currentPeriod is null
                ? []
                : await db.TeamPeriodScores
                    .Where(v => v.PeriodId == currentPeriod.PeriodId)
                    .ToDictionaryAsync(v => v.TeamId, v => new { v.ActivePoints, v.BenchPoints });

            var openSpots = await db.RosterSpots
                .Where(s => s.LeagueId == league.LeagueId && s.EndDate == null)
                .Select(s => new { s.TeamId, s.PlayerId })
                .ToListAsync();
            var nhlPoints = await Queries.NhlPointsAsync(
                db, league.Season, openSpots.Select(s => s.PlayerId).Distinct().ToList(),
                (await clock.StateAsync())?.AsOfDate);

            var normalized = username is null ? null : Queries.Normalize(username);
            var myTeam = normalized is null ? null : teams.FirstOrDefault(t => t.Owner == normalized);

            var commitments = await db.TeamCommitments
                .Where(c => c.LeagueId == league.LeagueId)
                .ToDictionaryAsync(c => c.TeamId);
            // Players already moving in an accepted trade: the sheet greys them
            // out rather than letting a GM build an offer the server will refuse.
            var engagedPlayers = (await TradeValidation.EngagedAssetsAsync(db, league.LeagueId)).PlayerIds;

            object[] myRoster = [];
            if (myTeam is not null)
            {
                var myPlayerIds = openSpots.Where(s => s.TeamId == myTeam.TeamId).Select(s => s.PlayerId).ToList();
                var players = await db.Players.Where(p => myPlayerIds.Contains(p.PlayerId)).ToListAsync();
                var caps = await Queries.CapHitsAsync(db, league.Season, myPlayerIds);
                var spotPoints = await db.RosterSpotTotals
                    .Where(v => v.TeamId == myTeam.TeamId)
                    .ToListAsync();
                var pointsByPlayer = spotPoints
                    .GroupBy(v => v.PlayerId)
                    .ToDictionary(g => g.Key, g => g.Sum(v => v.ActivePoints));

                myRoster = [.. players
                    .Select(p => new
                    {
                        id = p.PlayerId,
                        name = p.FullName,
                        position = p.Position,
                        team = p.TeamAbbrev,
                        status = p.Status,
                        capHit = caps.TryGetValue(p.PlayerId, out var c) ? c : (long?)null,
                        headshotUrl = p.HeadshotUrl,
                        points = pointsByPlayer.GetValueOrDefault(p.PlayerId),
                        nhlPoints = nhlPoints.GetValueOrDefault(p.PlayerId.ToString()),
                        engaged = engagedPlayers.Contains(p.PlayerId),
                    })
                    .OrderByDescending(x => x.points)
                    .Cast<object>()];
            }

            return Results.Ok(new
            {
                id = league.JoinCode,
                league.Name,
                league.Season,
                league.CapAmount,
                commissionerUsername = (await db.Users.FindAsync(league.CommissionerUserId))?.Username ?? "",
                ruleConfig = Dtos.RuleConfig(league, await Queries.ScaleAsync(db, league.LeagueId)),
                members = await db.LeagueMembers
                    .Where(m => m.LeagueId == league.LeagueId)
                    .Select(m => m.User!.Username).ToListAsync(),
                currentPeriod = currentPeriod is null ? null : Dtos.Period(currentPeriod, now),
                teams = teams
                    .Select(t =>
                    {
                        var s = byTeam.GetValueOrDefault(t.TeamId);
                        var week = currentWeek.GetValueOrDefault(t.TeamId);
                        var teamPlayers = openSpots.Where(o => o.TeamId == t.TeamId).Select(o => o.PlayerId).ToList();
                        return new
                        {
                            t.Name,
                            ownerUsername = t.Owner,
                            score = s?.Score ?? 0,
                            ptsPerGame = s is { RosterGamesPlayed: > 0 }
                                ? Math.Round(s.Score / s.RosterGamesPlayed, 2)
                                : (double?)null,
                            capTotal = s?.CapTotal ?? 0,
                            playerCount = s?.PlayerCount ?? 0,
                            // What accepted-but-unexecuted trades already commit
                            // this team to. The trade sheet validates against
                            // these, not the two above — otherwise its recap
                            // would show a number the server then contradicts.
                            engagedCapTotal = (s?.CapTotal ?? 0) + (commitments.GetValueOrDefault(t.TeamId)?.CapDelta ?? 0),
                            engagedPlayerCount = (s?.PlayerCount ?? 0) + (commitments.GetValueOrDefault(t.TeamId)?.CountDelta ?? 0),
                            playerNhlPoints = teamPlayers
                                .Where(id => nhlPoints.ContainsKey(id.ToString()))
                                .ToDictionary(id => id.ToString(), id => nhlPoints[id.ToString()]),
                            franchiseAbbrev = t.FranchiseAbbrev,
                            periodPoints = week?.ActivePoints ?? 0,
                            benchScore = week?.BenchPoints ?? 0,
                            finalizedScore = s?.FinalizedScore ?? 0,
                        };
                    })
                    .OrderByDescending(t => t.score)
                    .ThenBy(t => t.Name),
                myRoster,
            });
        });
    }

    private static async Task<string> UniqueCodeAsync(FantasyWarriorDbContext db)
    {
        for (var i = 0; i < 10; i++)
        {
            var code = JoinCodes.New();
            if (!await db.Leagues.AnyAsync(l => l.JoinCode == code)) return code;
        }
        throw new InvalidOperationException("Could not generate a unique join code.");
    }
}

public record LoginRequest(string? Username);
public record CreateLeagueRequest(string? Name, string? Username, string? TeamName, string? Season, long? CapAmount);
public record JoinLeagueRequest(string? Username, string? TeamName);
public record UpdateRulesRequest(string? Username, RuleConfig? RuleConfig);
