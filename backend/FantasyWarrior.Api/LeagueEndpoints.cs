using FantasyWarrior.Core.Messaging;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Leagues;
using FantasyWarrior.Data.Seasons;
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
            // The cap comes off the season being scored, not off the league —
            // it is a rule, and rules have a season. Resolved in memory because
            // the rules are one JSON document behind a value converter, and
            // "my leagues" is a handful of rows however many pools someone is in.
            var rows = await db.LeagueMembers
                .Where(m => m.User!.Username == normalized)
                .Select(m => new
                {
                    id = m.League!.JoinCode,
                    m.League.Name,
                    m.League.Season,
                    Rules = m.League.Seasons
                        .Where(s => s.Season == m.League.Season)
                        .Select(s => s.Rules)
                        .FirstOrDefault(),
                    members = m.League.Members.Count,
                })
                .ToListAsync();

            return Results.Ok(rows.Select(r => new
            {
                r.id,
                r.Name,
                r.Season,
                capAmount = r.Rules?.Cap.Max,
                r.members,
            }));
        });

        // The declared NHL calendar, newest first — what a league-creation
        // screen picks from instead of typing an eight-digit string. Public:
        // it is the NHL's schedule, not anyone's pool data.
        app.MapGet("/api/seasons", async (FantasyWarriorDbContext db) =>
        {
            var rows = await db.Seasons.AsNoTracking()
                .OrderByDescending(s => s.Season)
                .ToListAsync();

            // Season.Display is a pure string function with no SQL translation,
            // so the shaping happens here rather than in the query.
            return Results.Ok(rows.Select(s => new
            {
                season = s.Season,
                display = Season.Display(s.Season),
                regularSeasonStart = s.RegularSeasonStart,
                regularSeasonEnd = s.RegularSeasonEnd,
                playoffStart = s.PlayoffStart,
                playoffEnd = s.PlayoffEnd,
                // "We hold this season's games" — the difference between a
                // calendar that can be scored and one that is only declared.
                scheduleImported = s.ScheduleImportedUtc is not null,
            }));
        });

        app.MapPost("/api/leagues", async (
            CreateLeagueRequest req, FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { error = "Name and username are required." });

            // The column is free text, so "2025-2026" would create a phantom
            // season nothing else in the app could ever join to.
            if (req.Season is not null && !Season.IsValid(req.Season))
                return Results.BadRequest(new
                {
                    error = $"\"{req.Season}\" is not a valid NHL season. Expected a form like 20262027.",
                });

            // No season given: the one the declared calendar says we are in,
            // rather than a hardcoded string that goes stale every September.
            var season = req.Season
                ?? await SeasonLookup.CurrentOrGuessAsync(db, await clock.TodayEtAsync());

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
                Season = season,
                JoinCode = await UniqueCodeAsync(db),
                CommissionerUserId = user.UserId,
                CreatedUtc = now,
            };
            db.Leagues.Add(league);
            await db.SaveChangesAsync();

            var rules = RuleSetDefaults.ForNewLeague();
            rules.Cap.Max = req.CapAmount;

            // Its first season, opened here rather than by a separate command.
            // A league with no LeagueSeason row has nowhere to keep its rules,
            // and every consumer refuses on that — so creating one without it
            // would produce a league that cannot trade, score or draft.
            db.LeagueSeasons.Add(new LeagueSeason
            {
                LeagueId = league.LeagueId,
                Season = season,
                Number = 1,
                Phase = LeagueSeasonPhase.Preparing,
                Rules = rules,
                StartedUtc = now,
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
            if (string.IsNullOrWhiteSpace(req.Username) || req.RuleSet is null)
                return Results.BadRequest(new { error = "Username and ruleSet are required." });

            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var commissioner = await db.Users.FindAsync(league.CommissionerUserId);
            if (commissioner?.Username != Queries.Normalize(req.Username))
                return Results.Json(new { error = "Only the commissioner can change the rules." }, statusCode: 403);

            var incoming = req.RuleSet;
            incoming.Version = RuleSetDefaults.CurrentVersion;

            LeagueSeason target;
            try
            {
                // The season being prepared, never a closed one: editing a
                // Complete season's rules would restate what a finished season
                // was played under, which is the one thing storing rules per
                // season exists to prevent.
                target = await RuleSetResolver.ForEditingAsync(db, league.LeagueId);
            }
            catch (RuleSetUnavailableException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            // The franchise slot is a fact about how the league was built, not a
            // setting: turning it on creates no Équipe spots and turning it off
            // deletes none, so accepting a change here would be accepting a
            // no-op that looks like a rule.
            incoming.Roster.FranchiseSlot = target.Rules.Roster.FranchiseSlot;

            var errors = RuleSetValidation.Validate(incoming);
            if (errors.Count > 0)
                return Results.BadRequest(new { error = string.Join(" ", errors), errors });

            target.Rules = incoming;
            await db.SaveChangesAsync();

            // Saved is not the same as enforced. A commissioner may record the
            // pool's real rules before the code catches up — but the answer says
            // which of them are inert, so nothing here is silently ignored.
            var unsupported = RuleSetCapabilities.Unsupported(incoming);

            return Results.Ok(new
            {
                ok = true,
                season = target.Season,
                unsupported = unsupported.Select(g => new { path = g.Path, message = g.Message }),
                note = "Applies from the next nightly scoring run. Weeks already banked keep the "
                     + "scale they were scored under.",
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

            // Player spots only: everything below is NHL points and cap hits,
            // neither of which an Équipe spot has. The franchises come back on
            // their own a few lines down.
            var openSpots = await db.RosterSpots
                .Where(s => s.LeagueId == league.LeagueId && s.EndDate == null && s.PlayerId != null)
                .Select(s => new { s.TeamId, PlayerId = s.PlayerId!.Value })
                .ToListAsync();
            var nhlPoints = await Queries.NhlPointsAsync(
                db, league.Season, openSpots.Select(s => s.PlayerId).Distinct().ToList(),
                (await clock.StateAsync())?.AsOfDate);

            // Who owns which franchise *right now* — the Équipe spot, not
            // Teams.FranchiseAbbrev. The two start equal and are meant to be
            // able to diverge: the club you are is not the club you own (Nick,
            // 2026-08-05). The trade sheet needs the one that can move.
            var engagedAssets = await TradeValidation.EngagedAssetsAsync(db, league.LeagueId);
            var franchises = (await db.RosterSpots
                .Where(s => s.LeagueId == league.LeagueId && s.EndDate == null && s.FranchiseAbbrev != null)
                .Select(s => new
                {
                    s.TeamId,
                    abbrev = s.FranchiseAbbrev!,
                    name = s.Franchise!.Name,
                    logoUrl = s.Franchise.LogoUrl,
                })
                .ToListAsync())
                .Select(s => new
                {
                    s.TeamId,
                    s.abbrev,
                    s.name,
                    s.logoUrl,
                    // Already promised away by an accepted trade. Players and
                    // picks have carried this since trades were built; the
                    // franchise arrived tradable without it, so the sheet let a
                    // GM build an offer the server would then refuse.
                    engaged = engagedAssets.FranchiseAbbrevs.Contains(s.abbrev),
                })
                .ToDictionary(s => s.TeamId);

            var normalized = username is null ? null : Queries.Normalize(username);
            var myTeam = normalized is null ? null : teams.FirstOrDefault(t => t.Owner == normalized);

            // Players already moving in an accepted trade: the sheet greys them
            // out rather than letting a GM build an offer the server will refuse.
            var engagedPlayers = engagedAssets.PlayerIds;

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
                    .Where(v => v.PlayerId != null)
                    .GroupBy(v => v.PlayerId!.Value)
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

            var activeSeason = await Queries.ActiveLeagueSeasonAsync(db, league.LeagueId);

            // A league whose rules were never converted still has to render:
            // this is the screen every session opens on, and a 500 here would
            // take the whole app down rather than one panel. The defaults are
            // marked unwritten, which is what the panel shows.
            RuleSet scoringRules;
            try
            {
                scoringRules = await RuleSetResolver.ForScoringAsync(db, league);
            }
            catch (RuleSetUnavailableException)
            {
                scoringRules = RuleSetDefaults.ForNewLeague();
            }

            return Results.Ok(new
            {
                id = league.JoinCode,
                league.Name,
                league.Season,
                capAmount = scoringRules.Cap.Max,
                commissionerUsername = (await db.Users.FindAsync(league.CommissionerUserId))?.Username ?? "",
                // What phase this league's current season sits in. Here rather
                // than on its own route because it decides whether the Draft
                // tab exists at all, and the nav must not need a second request
                // to draw itself.
                activeSeason = activeSeason is null
                    ? null
                    : new { number = activeSeason.Number, season = activeSeason.Season, phase = activeSeason.Phase.ToString() },
                // The rules the league is being SCORED under. During the
                // off-season that is not the same document the rules panel
                // edits, which is the prepared season's -- and that difference
                // is the point, not a discrepancy.
                ruleSet = Dtos.RuleSet(scoringRules),
                // Which of those rules nothing enforces, so the panel can badge
                // them rather than let a commissioner believe they are live.
                unsupported = RuleSetCapabilities.Unsupported(scoringRules)
                    .Select(g => new { path = g.Path, message = g.Message }),
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
                            // How much of capTotal is the league's DefaultCapHit
                            // standing in for a contract nobody has on file. The
                            // total reads as authoritative once those are folded
                            // in, so the count travels with it.
                            unknownContracts = s?.UnknownContracts ?? 0,
                            playerCount = s?.PlayerCount ?? 0,
                            // What accepted-but-unlanded trades already commit
                            // this team to. The trade sheet validates against
                            // these, not the two above — otherwise its recap
                            // would show a number the server then contradicts.
                            // Columns on the same view row rather than a delta
                            // added here, so the two figures cannot drift.
                            engagedCapTotal = s?.EngagedCapTotal ?? 0,
                            engagedPlayerCount = s?.EngagedPlayerCount ?? 0,
                            playerNhlPoints = teamPlayers
                                .Where(id => nhlPoints.ContainsKey(id.ToString()))
                                .ToDictionary(id => id.ToString(), id => nhlPoints[id.ToString()]),
                            // The team's own identity, which never moves.
                            franchiseAbbrev = t.FranchiseAbbrev,
                            // The franchise it *holds* — the Équipe roster
                            // spot, which is what a trade can move. Null for a
                            // league that does not use the slot.
                            franchise = franchises.GetValueOrDefault(t.TeamId),
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

        // The palmarès: one row per season this league has ever played,
        // newest first, with its champion — the first screen that pays off
        // keeping RosterAssignments forever instead of clearing them at
        // rollover (offseason.md).
        app.MapGet("/api/leagues/{leagueId}/seasons", async (string leagueId, FantasyWarriorDbContext db) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var seasons = await db.LeagueSeasons
                .Where(s => s.LeagueId == league.LeagueId)
                .OrderByDescending(s => s.Number)
                .Select(s => new
                {
                    number = s.Number,
                    season = s.Season,
                    phase = s.Phase.ToString(),
                    championTeamName = s.ChampionTeam!.Name,
                    completedUtc = s.CompletedUtc,
                })
                .ToListAsync();

            return Results.Ok(seasons);
        });

        // Who has actually shown up — the commissioner's answer to "did a real
        // person log in, or did I just look at their team?"
        //
        // LastLoginUtc was written at every login since the SQL rebuild and read
        // by nothing at all, which is why the first genuine outside login
        // (steeve, 2026-08-25) could only be inferred from a declined trade.
        // The two timestamps answer different questions and both are here:
        // LoginUtc is a deliberate act, SeenUtc is any traffic at all.
        //
        // Commissioner-only, and not because the data is sensitive — it is a
        // diagnostic with no screen, and a per-GM last-login list on a public
        // route is a surveillance feature nobody asked for.
        app.MapGet("/api/leagues/{leagueId}/activity", async (
            string leagueId, string? username, FantasyWarriorDbContext db, PresenceService presence) =>
        {
            if (string.IsNullOrWhiteSpace(username))
                return Results.BadRequest(new { error = "Username is required." });

            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var commissioner = await db.Users.FindAsync(league.CommissionerUserId);
            if (commissioner?.Username != Queries.Normalize(username))
                return Results.Json(new { error = "Only the commissioner can read league activity." }, statusCode: 403);

            var now = DateTime.UtcNow;
            var members = await db.LeagueMembers
                .Where(m => m.LeagueId == league.LeagueId)
                .Select(m => new
                {
                    m.UserId,
                    m.User!.Username,
                    m.User.DisplayName,
                    m.User.CreatedUtc,
                    m.User.LastLoginUtc,
                    m.User.LastSeenUtc,
                })
                .ToListAsync();

            return Results.Ok(members
                .Select(m => new
                {
                    username = m.Username,
                    displayName = m.DisplayName,
                    online = presence.IsOnline(league.LeagueId, m.UserId),
                    // Null means this account has never once been logged into,
                    // however recently something stamped it as seen.
                    lastLoginUtc = m.LastLoginUtc,
                    lastLoginLabel = Presence.Describe(false, m.LastLoginUtc, now),
                    lastSeenUtc = m.LastSeenUtc,
                    lastSeenLabel = Presence.Describe(
                        presence.IsOnline(league.LeagueId, m.UserId), m.LastSeenUtc, now),
                    createdUtc = m.CreatedUtc,
                })
                // Whoever turned up most recently first: this list is read to
                // find out who is alive, not to look someone up.
                .OrderByDescending(m => m.lastLoginUtc ?? DateTime.MinValue)
                .ThenBy(m => m.username, StringComparer.Ordinal));
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
public record UpdateRulesRequest(string? Username, RuleSet? RuleSet);
