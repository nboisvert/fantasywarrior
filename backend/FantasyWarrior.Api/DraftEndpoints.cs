using FantasyWarrior.Core.Drafts;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Leagues;
using FantasyWarrior.Data.Rosters;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>
/// The draft room.
///
/// <b>One room, two drafts.</b> The <c>Drafting</c> phase runs the steal rounds
/// and then the rookie / free-agent rounds back to back. They share a turn
/// engine and a selection log and differ only in where a turn comes from and
/// who is available - which is the whole of what "generic over draft type"
/// means here. A third kind is a third branch in <see cref="DraftPool"/> and
/// nothing else.
///
/// <b>There is no clock</b> (Nick, 2026-08-25). The GM on the clock picks
/// whenever they get to it; nobody is timed out and nothing auto-picks. That is
/// a product decision, and it is also what lets the draft live entirely inside
/// request handling: no hosted service, no timer, and the Container App keeps
/// scaling to zero between picks.
/// </summary>
public static class DraftEndpoints
{
    /// <summary>Rows returned by the available list unless asked otherwise.</summary>
    private const int DefaultLimit = 200;

    public static void Map(WebApplication app)
    {
        // The room's one read: everything above the available list.
        app.MapGet("/api/leagues/{leagueId}/draft", async (
            string leagueId, string? username, FantasyWarriorDbContext db) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var ctx = await DraftContextLoader.LoadAsync(db, league);
            if (ctx is null) return Results.Ok(new { running = false, phase = (string?)null });

            // Answering "not running" rather than 404 is deliberate: this one
            // call tells the client both whether the Draft tab should exist and
            // what goes on it, so the nav never needs a second request.
            if (ctx.Season.Phase != LeagueSeasonPhase.Drafting)
                return Results.Ok(new { running = false, phase = ctx.Season.Phase.ToString() });

            var me = username is null ? null : await Queries.TeamAsync(db, league.LeagueId, Queries.Normalize(username));
            return Results.Ok(await StateAsync(db, ctx, me));
        });

        // The pool for the turn on the clock. Recomputed every call - see
        // DraftContextLoader.AvailableAsync for why it cannot be cached.
        app.MapGet("/api/leagues/{leagueId}/draft/available", async (
            string leagueId, string? username, string? search, string? pos, int? limit,
            FantasyWarriorDbContext db) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var ctx = await DraftContextLoader.LoadAsync(db, league);
            if (ctx is null || ctx.Season.Phase != LeagueSeasonPhase.Drafting)
                return Results.Json(new { error = "The draft is not open." }, statusCode: 409);

            var turn = ctx.OnTheClock;
            if (turn is null) return Results.Ok(Array.Empty<object>());

            var rows = await DraftContextLoader.AvailableAsync(db, ctx, turn);

            if (!string.IsNullOrWhiteSpace(search))
                rows = [.. rows.Where(r => r.ShortName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))];

            if (!string.IsNullOrWhiteSpace(pos) && pos.Trim().ToUpperInvariant() is { } group && group != "ALL")
                rows = [.. rows.Where(r => r.Candidate.PositionGroup == group)];

            return Results.Ok(rows
                .Take(limit is > 0 and <= 500 ? limit.Value : DefaultLimit)
                .Select(Row));
        });

        // The pick.
        app.MapPost("/api/leagues/{leagueId}/draft/selections", async (
            string leagueId, DraftSelectRequest req, FantasyWarriorDbContext db,
            SimulationClockService clock, IHubContext<LiveHub> hub, ILoggerFactory logs) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { error = "Username is required." });

            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var ctx = await DraftContextLoader.LoadAsync(db, league);
            if (ctx is null || ctx.Season.Phase != LeagueSeasonPhase.Drafting)
                return Results.Json(new { error = "The draft is not open." }, statusCode: 409);

            var me = await Queries.TeamAsync(db, league.LeagueId, Queries.Normalize(req.Username));
            if (me is null) return Results.Json(new { error = "You have no team in this league." }, statusCode: 403);

            var turn = ctx.OnTheClock;
            if (turn is null) return Results.Json(new { error = "The draft is finished." }, statusCode: 409);

            // The whole of the turn enforcement. Under the current no-auth model
            // this stops an honest mistake, not a determined GM - see
            // scoring-model.md section 11. Every selection is pushed to all 14
            // GMs the instant it lands, which is the real deterrent in a pool
            // this size.
            if (turn.TeamId != me.TeamId)
                return Results.Json(
                    new { error = $"It is {ctx.TeamName(turn.TeamId)}'s pick." }, statusCode: 403);

            // The client echoes back the turn it was looking at. A stale tab
            // gets a clean "someone picked while you were looking" instead of
            // silently taking a turn that has moved on.
            if (req.ExpectedOverallIndex is { } expected && expected != turn.OverallIndex)
                return Results.Json(
                    new { error = "Someone picked while you were looking. Refreshing." }, statusCode: 409);

            var today = await clock.TodayEtAsync();

            // A passed turn: no player, nobody robbed. Necessary, not a
            // courtesy - 14 teams times 2 losses is exactly the 28 turns of the
            // steal segment, so a GM late in the order can face an empty pool
            // and would otherwise deadlock the draft.
            if (req.PlayerId is not { } playerId)
                return await CommitAsync(db, hub, logs, ctx, me, turn, null, null, today, null);

            var pool = await DraftContextLoader.AvailableAsync(db, ctx, turn);
            var chosen = pool.FirstOrDefault(r => r.Candidate.PlayerId == playerId);
            if (chosen is null)
            {
                // Re-derive the reason rather than saying "not available": the
                // GM is owed the rule, and the pool was just recomputed so the
                // reason is current.
                var reason = await ReasonAsync(db, ctx, turn, playerId);
                return Results.BadRequest(new { error = reason });
            }

            var victimTeamId = chosen.Candidate.OwnerTeamId;

            var errors = DraftRules
                .ValidateSelection(
                    pickerTeamName: me.Name,
                    pickerCapBefore: await CapOfAsync(db, me.TeamId),
                    pickerCountBefore: await CountOfAsync(db, me.TeamId),
                    incomingCapHit: chosen.CapHit,
                    defaultCapHit: ctx.Rules.Cap.DefaultCapHit,
                    capAmount: ctx.Rules.Cap.Max)
                .ToList();

            if (victimTeamId is { } victim)
                errors.AddRange(DraftRules.ValidateLoss(
                    ctx.TeamName(victim), ctx.LossesOf(victim), ctx.MaxLossesPerTeam));

            if (errors.Count > 0)
                return Results.BadRequest(new { error = string.Join(" ", errors), errors });

            return await CommitAsync(db, hub, logs, ctx, me, turn, playerId, victimTeamId, today, chosen);
        });

        // Commissioner: fill every GM's protection slate with his best players.
        //
        // A stand-in for the protection screen, which is not built. Without it a
        // draft filters on auto-protection alone and the steal pool is every
        // veteran in the league — which is not what the room is meant to be
        // judged against. It is a default, not a decision: the real screen is
        // what a GM who disagrees will use.
        app.MapPost("/api/leagues/{leagueId}/protections/autofill", async (
            string leagueId, bool? preview, DraftCommandRequest req,
            FantasyWarriorDbContext db, SimulationClockService clock,
            IHubContext<LiveHub> hub, ILoggerFactory logs) =>
        {
            var guard = await CommissionerAsync(db, leagueId, req.Username);
            if (guard.Error is not null) return guard.Error;
            var league = guard.League!;

            var season = await Queries.ActiveLeagueSeasonAsync(db, league.LeagueId);
            if (season is null) return Results.BadRequest(new { error = "This league has no active season." });

            // Protections are worth exactly one off-season and are wiped on the
            // way into InSeason, so writing them at any other time would be
            // writing something the next phase transition erases.
            if (season.Phase != LeagueSeasonPhase.Protecting)
                return Results.BadRequest(new
                {
                    error = $"Protections are set during Protecting, not {season.Phase}.",
                });

            if (season.Rules.IsUnwritten)
                return Results.BadRequest(new
                {
                    error = "This league's rules were never written. Run `rules-backfill`.",
                });

            if (season.Rules.Protection.Slots is not { } slots)
                return Results.BadRequest(new
                {
                    error = "Set the league's protection slots first, in the rules panel.",
                });

            // The season that just ended, derived rather than hardcoded:
            // Leagues.Season still points at it, but the prepared row is the one
            // that says which off-season this is.
            var lastSeason = Season.Previous(season.Season);

            // Bounded to the simulated day like every other season total. The
            // database holds the whole 2025-26 schedule; unbounded, a replay
            // sitting in December would rank players on games nobody has played.
            var asOf = (await clock.StateAsync())?.AsOfDate;

            // Ranked under the scale that season was actually scored under, not
            // under next season's: points belong to the rules they were earned
            // under, which is the whole reason a scale change restates nothing.
            // A league in its very first off-season has no prior document, and
            // the prepared season's scale is then the only one there is.
            var rankingScale = await RankingScaleAsync(db, league, lastSeason, season.Rules);

            // The reading, the ranking and the write all live in ProtectionSlate
            // so that clone-league produces the same slate this button does.
            var slate = await ProtectionSlate.AutofillAsync(
                db, league.LeagueId, lastSeason, season.Rules.Protection, rankingScale,
                asOf, write: preview != true);

            var body = new
            {
                ok = true,
                slots,
                teams = slate.Teams,
                protectedCount = slate.Protected,
                freeCount = slate.Free,
                exposedCount = slate.Exposed,
            };

            if (preview == true) return Results.Ok(body);

            await PushAsync(hub, logs, league, null,
                $"{slate.Protected} players protected, {slots} per team.");

            return Results.Ok(body);
        });

        // Every team's protection slate — the room's Protections pane.
        //
        // **Public to the whole league** (Nick, 2026-08-29). It was an open
        // product question in offseason.md, and hiding it buys
        // nothing: the steal pool already gives it away by omission, since a
        // veteran who is missing from the available list is a veteran somebody
        // protected. Making it explicit costs no secrecy and gives the pool
        // something to argue about, which is the point of the feature.
        //
        // **Not phase-gated.** It is most useful during Protecting, before the
        // room opens, and it stays honest afterwards because a steal only ever
        // takes an exposed player — nothing here changes once the draft starts.
        app.MapGet("/api/leagues/{leagueId}/protections", async (
            string leagueId, FantasyWarriorDbContext db) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            // Same filter as the autofill and the steal pool: held spots,
            // players only — a franchise is not draftable and needs no shelter.
            var held = await db.RosterSpots
                .AsNoTracking()
                .Where(s => s.LeagueId == league.LeagueId && s.PlayerId != null)
                .Where(RosterWindow.Committed())
                .Select(s => new
                {
                    s.TeamId,
                    PlayerId = s.PlayerId!.Value,
                    s.ProtectionStatus,
                    s.Player!.FirstName,
                    s.Player.LastName,
                    s.Player.Position,
                    s.Player.PositionGroup,
                    s.Player.CareerNhlGames,
                })
                .ToListAsync();

            // The prepared season's rules: this pane describes the draft about
            // to run, so its slots and its auto-protection bars are the ones
            // that decide who is exposed.
            var rules = await RuleSetResolver.ForActiveSeasonAsync(db, league.LeagueId);

            var caps = await Queries.CapHitsAsync(
                db, league.Season, held.Select(h => h.PlayerId).Distinct().ToList());

            var teams = await db.Teams
                .AsNoTracking()
                .Where(t => t.LeagueId == league.LeagueId)
                .Include(t => t.Owner)
                .ToListAsync();

            var byTeam = held
                .Select(h => new
                {
                    h.TeamId,
                    h.PlayerId,
                    ShortName = DraftFormat.ShortName(h.FirstName, h.LastName),
                    h.Position,
                    h.PositionGroup,
                    CapHit = caps.TryGetValue(h.PlayerId, out var cap) ? cap : rules.Cap.DefaultCapHit,
                    Kind = ProtectionRules.KindOf(
                        h.PositionGroup, h.CareerNhlGames,
                        h.ProtectionStatus == RosterProtectionStatus.Protected,
                        rules.Protection.Auto),
                })
                .ToLookup(h => h.TeamId);

            return Results.Ok(new
            {
                slots = rules.Protection.Slots,
                teams = teams
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(t =>
                    {
                        var roster = byTeam[t.TeamId].ToList();
                        return new
                        {
                            teamName = t.Name,
                            ownerUsername = t.Owner?.Username,
                            protectedCount = roster.Count(r => r.Kind == ProtectionKind.ByGm),
                            autoCount = roster.Count(r => r.Kind is ProtectionKind.Auto or ProtectionKind.Unknown),
                            exposedCount = roster.Count(r => r.Kind == ProtectionKind.Exposed),
                            // The slate itself: everyone out of reach, the GM's
                            // own picks first. An exposed player is the absence
                            // of a protection, so he is a count, not a row.
                            players = roster
                                .Where(r => r.Kind != ProtectionKind.Exposed)
                                .OrderBy(r => r.Kind == ProtectionKind.ByGm ? 0 : 1)
                                .ThenByDescending(r => r.CapHit)
                                .Select(r => new
                                {
                                    playerId = r.PlayerId,
                                    shortName = r.ShortName,
                                    position = r.Position,
                                    positionGroup = r.PositionGroup,
                                    capHit = r.CapHit,
                                    status = r.Kind switch
                                    {
                                        ProtectionKind.ByGm => "protected",
                                        ProtectionKind.Unknown => "unknown",
                                        _ => "auto",
                                    },
                                }),
                        };
                    }),
            });
        });

        // Commissioner: freeze the order and open the room.
        app.MapPost("/api/leagues/{leagueId}/draft/open", async (
            string leagueId, DraftCommandRequest req, FantasyWarriorDbContext db,
            IHubContext<LiveHub> hub, ILoggerFactory logs) =>
        {
            var guard = await CommissionerAsync(db, leagueId, req.Username);
            if (guard.Error is not null) return guard.Error;
            var league = guard.League!;

            var season = await Queries.ActiveLeagueSeasonAsync(db, league.LeagueId);
            if (season is null) return Results.BadRequest(new { error = "This league has no active season." });

            if (!SeasonPhaseRules.CanTransition(season.Phase, LeagueSeasonPhase.Drafting))
                return Results.BadRequest(new
                {
                    error = $"A draft opens from Protecting, not {season.Phase}.",
                });

            if (season.Rules.IsUnwritten)
                return Results.BadRequest(new
                {
                    error = "This league's rules were never written. Run `rules-backfill`.",
                });

            var rules = season.Rules;

            // The failure this whole endpoint used to have. It read neither
            // ProtectionSlots nor StealRounds, so a league that had decided both
            // and entered neither opened anyway — and `?? 0` downstream turned
            // that into an all-rookie draft with uncapped losses, silently.
            // Everything below refuses instead, and says which rule is at fault.
            foreach (var gap in RuleSetCapabilities.Unsupported(rules)
                         .Where(g => g.Path.StartsWith("draft.") || g.Path.StartsWith("protection.")))
                return Results.BadRequest(new
                {
                    error = $"This league's {gap.Path} is set to something the draft room cannot run. "
                          + gap.Message,
                });

            if (rules.Draft.RookieRounds is not > 0)
                return Results.BadRequest(new { error = "Set the league's draft rounds first." });

            if (rules.Draft.Steal.Rounds > 0)
            {
                if (rules.Protection.Slots is null)
                    return Results.BadRequest(new
                    {
                        error = "This league has steal rounds but no protection slots, so every "
                              + "veteran would be exposed. Set the protection slots first.",
                    });

                // Slots set and nothing protected means the slate was never
                // written. Opening here would run the steal segment against a
                // league where the only shelter is the auto-protection bar.
                var anyProtected = await db.RosterSpots.AnyAsync(
                    s => s.LeagueId == league.LeagueId
                         && s.ProtectionStatus == RosterProtectionStatus.Protected);
                if (!anyProtected && rules.Protection.Slots > 0)
                    return Results.BadRequest(new
                    {
                        error = "Nobody is protected yet, and this draft has steal rounds. "
                              + "Run the protection autofill first.",
                    });
            }

            var year = Season.StartYear(season.Season);

            var picks = await db.DraftPicks
                .Where(p => p.LeagueId == league.LeagueId && p.Year == year)
                .ToListAsync();

            var teamIds = await db.Teams
                .Where(t => t.LeagueId == league.LeagueId)
                .Select(t => t.TeamId)
                .ToListAsync();

            // A team added after the picks were generated would have no
            // entitlement and no steal turn, and would silently shift the
            // derived team count for everyone else.
            if (picks.Count != teamIds.Count * rules.Draft.RookieRounds)
                return Results.BadRequest(new
                {
                    error = $"Expected {teamIds.Count * rules.Draft.RookieRounds} picks for {year} "
                          + $"but found {picks.Count}. Run draft-picks-init first.",
                });

            if (await db.DraftSelections.AnyAsync(s => s.LeagueSeasonId == season.LeagueSeasonId))
                return Results.BadRequest(new
                {
                    error = "This draft has already started. Reopening it would renumber the order.",
                });

            // The only moment the standings are read. Leagues.Season still
            // points at the season that just finished, so these are its final
            // numbers - and freezing them here is what stops the order from
            // moving under the draft later.
            var standings = await db.Standings
                .Where(s => s.LeagueId == league.LeagueId)
                .Select(s => new { s.TeamId, s.Score })
                .ToListAsync();

            var order = DraftOrder.ReverseStandings(
                teamIds.Select(id => (id, standings.FirstOrDefault(s => s.TeamId == id)?.Score ?? 0d)));

            var slotByTeam = order
                .Select((teamId, index) => (teamId, pickInRound: index + 1))
                .ToDictionary(x => x.teamId, x => x.pickInRound);

            foreach (var pick in picks)
                pick.PickInRound = slotByTeam.GetValueOrDefault(pick.OriginalTeamId);

            season.Phase = LeagueSeasonPhase.Drafting;
            await db.SaveChangesAsync();

            var ctx = await DraftContextLoader.LoadAsync(db, league);
            await PushAsync(hub, logs, league, ctx is null ? null : await StateAsync(db, ctx, null),
                $"The {year} draft is open.");

            return Results.Ok(new { ok = true, year, order = order.Select(ctx!.TeamName) });
        });

        // Commissioner: close the room, whether or not every turn was used.
        app.MapPost("/api/leagues/{leagueId}/draft/close", async (
            string leagueId, DraftCommandRequest req, FantasyWarriorDbContext db,
            IHubContext<LiveHub> hub, ILoggerFactory logs) =>
        {
            var guard = await CommissionerAsync(db, leagueId, req.Username);
            if (guard.Error is not null) return guard.Error;
            var league = guard.League!;

            var ctx = await DraftContextLoader.LoadAsync(db, league);
            if (ctx is null || ctx.Season.Phase != LeagueSeasonPhase.Drafting)
                return Results.BadRequest(new { error = "This league is not drafting." });

            // Unused turns are fine and are not an error state: an exposed
            // player nobody claimed simply stays where he was - he never moved.
            var unused = ctx.Picks.Count(p => !p.Used)
                       + Math.Max(0, DraftSegments.StealTurnCount(ctx.OrderedTeamIds.Count, ctx.StealRounds)
                                     - ctx.Selections.Count(s => s.Segment == DraftSegment.Steal));

            ctx.Season.Phase = LeagueSeasonPhase.PreSeason;
            await db.SaveChangesAsync();

            await PushAsync(hub, logs, league, null, "The draft is closed.");

            return Results.Ok(new { ok = true, phase = ctx.Season.Phase.ToString(), unusedTurns = unused });
        });
    }

    // ---- the write, and the three races it has to survive ----

    private static async Task<IResult> CommitAsync(
        FantasyWarriorDbContext db, IHubContext<LiveHub> hub, ILoggerFactory logs,
        DraftContext ctx, Team me, DraftTurn turn, long? playerId, int? victimTeamId,
        DateOnly today, DraftPoolRow? chosen)
    {
        var selection = new DraftSelection
        {
            LeagueSeasonId = ctx.Season.LeagueSeasonId,
            OverallIndex = turn.OverallIndex,
            Segment = turn.Segment,
            Round = turn.Round,
            TeamId = me.TeamId,
            PlayerId = playerId,
            StolenFromTeamId = playerId is null ? null : victimTeamId,
            DraftPickId = turn.DraftPickId,
            MadeUtc = DateTime.UtcNow,
        };

        try
        {
            // Through the execution strategy because retries are enabled: the
            // serverless tier drops connections on resume and a retry must
            // replay the whole selection rather than half of it.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();

                db.DraftSelections.Add(selection);

                if (playerId is { } player)
                {
                    if (victimTeamId is { } victim)
                        await RosterChange.ApplyAsync(
                            db, ctx.League.LeagueId, victim,
                            playersOut: [player], playersIn: [],
                            RosterSpotStartReason.Draft, RosterSpotEndReason.Draft, today);

                    await RosterChange.ApplyAsync(
                        db, ctx.League.LeagueId, me.TeamId,
                        playersOut: [], playersIn: [player],
                        RosterSpotStartReason.Draft, RosterSpotEndReason.Draft, today,
                        draftPickId: turn.DraftPickId);

                    if (turn.DraftPickId is { } pickId)
                    {
                        var pick = await db.DraftPicks.FindAsync(pickId);
                        if (pick is not null)
                        {
                            pick.PlayerId = player;
                            pick.UsedUtc = DateTime.UtcNow;
                        }
                    }
                }
                else if (turn.DraftPickId is { } passedPick)
                {
                    // A passed rookie turn still spends its entitlement -
                    // otherwise the same pick would come back round forever.
                    var pick = await db.DraftPicks.FindAsync(passedPick);
                    if (pick is not null) pick.UsedUtc = DateTime.UtcNow;
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Three different races land here and all three mean the same thing
            // to a GM: the board moved. UX_DraftSelections_OneSelectionPerTurn
            // catches two people on one turn, UX_DraftSelections_OnePerPick two
            // people burning one entitlement, and
            // UX_RosterSpots_OneOpenSpotPerPlayerPerLeague two people taking one
            // player. A 500 here would read as "the app ate my pick".
            return Results.Json(
                new { error = "Someone just picked. Refreshing the board." }, statusCode: 409);
        }

        var fresh = await DraftContextLoader.LoadAsync(db, ctx.League);
        var state = fresh is null ? null : await StateAsync(db, fresh, me);

        var text = playerId is null
            ? $"{me.Name} passed."
            : victimTeamId is { } v
                ? $"{me.Name} took {chosen!.ShortName} from {ctx.TeamName(v)}."
                : $"{me.Name} drafted {chosen!.ShortName}.";

        await PushAsync(hub, logs, ctx.League, state, text);

        return Results.Ok(state);
    }

    /// <summary>
    /// 2601 and 2627 are SQL Server's "duplicate key" pair - a filtered unique
    /// index reports the first, a primary or unique constraint the second.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    // ---- reads ----

    private static async Task<object> StateAsync(
        FantasyWarriorDbContext db, DraftContext ctx, Team? me)
    {
        var turn = ctx.OnTheClock;
        var stealTurns = DraftSegments.StealTurnCount(ctx.OrderedTeamIds.Count, ctx.StealRounds);

        var players = await PlayerRefsAsync(db, ctx);

        // The whole board, made turns and unmade ones together (Nick,
        // 2026-08-29). A feed of the last dozen picks answered "what just
        // happened"; a pool also wants "what is coming" — whose turn is three
        // from now, and how long until mine. Both questions are the same list
        // read from different ends, so there is one list.
        var board = ctx.Selections
            .OrderBy(s => s.OverallIndex)
            .Select(s => Selection(ctx, s, players))
            .Concat(DraftOrder
                .Remaining(ctx.OrderedTeamIds, ctx.StealRounds, ctx.Picks, ctx.SelectionsMade)
                .Select(t => Upcoming(ctx, t)))
            .ToList();

        return new
        {
            running = true,
            phase = ctx.Season.Phase.ToString(),
            year = Season.StartYear(ctx.Season.Season),
            seasonNumber = ctx.Season.Number,
            stealRounds = ctx.StealRounds,
            draftRounds = ctx.Rules.Draft.RookieRounds ?? 0,
            maxLossesPerTeam = ctx.MaxLossesPerTeam,
            segment = turn?.Segment.ToString().ToLowerInvariant(),
            round = turn?.Round,
            totalTurns = stealTurns + ctx.Picks.Count,
            turnsMade = ctx.SelectionsMade,
            onTheClock = turn is null ? null : new
            {
                overallIndex = turn.OverallIndex,
                segment = turn.Segment.ToString().ToLowerInvariant(),
                round = turn.Round,
                pickInRound = turn.PickInRound,
                teamName = ctx.TeamName(turn.TeamId),
                ownerUsername = ctx.OwnerUsername(turn.TeamId),
            },
            isMyTurn = me is not null && turn?.TeamId == me.TeamId,
            turnsUntilMine = me is null
                ? null
                : DraftOrder.TurnsUntil(
                    ctx.OrderedTeamIds, ctx.StealRounds, ctx.Picks, ctx.SelectionsMade, me.TeamId),
            myTeam = me is null ? null : new
            {
                teamName = me.Name,
                losses = ctx.LossesOf(me.TeamId),
                takes = ctx.TakesOf(me.TeamId),
            },
            teams = ctx.TeamsById.Values
                .Select(t => new
                {
                    teamName = t.Name,
                    ownerUsername = t.Owner?.Username,
                    losses = ctx.LossesOf(t.TeamId),
                    takes = ctx.TakesOf(t.TeamId),
                })
                .OrderBy(t => t.teamName, StringComparer.OrdinalIgnoreCase),
            board,
        };
    }

    /// <summary>Names for every player already selected, in one query.</summary>
    private static async Task<Dictionary<long, (string Short, string Position, string Group)>> PlayerRefsAsync(
        FantasyWarriorDbContext db, DraftContext ctx)
    {
        var ids = ctx.Selections.Where(s => s.PlayerId is not null).Select(s => s.PlayerId!.Value).ToList();
        if (ids.Count == 0) return [];

        var rows = await db.Players
            .AsNoTracking()
            .Where(p => ids.Contains(p.PlayerId))
            .Select(p => new { p.PlayerId, p.FirstName, p.LastName, p.Position, p.PositionGroup })
            .ToListAsync();

        return rows.ToDictionary(
            p => p.PlayerId,
            p => (DraftFormat.ShortName(p.FirstName, p.LastName), p.Position, p.PositionGroup));
    }

    /// <summary>
    /// A turn already taken. Same shape as <see cref="Upcoming"/> — the board is
    /// one list and a row's only difference is whether <c>done</c> is set, so a
    /// screen never has to branch on which half it is reading.
    /// </summary>
    private static object Selection(
        DraftContext ctx, DraftSelection s,
        IReadOnlyDictionary<long, (string Short, string Position, string Group)> players) => new
        {
            overallIndex = s.OverallIndex,
            segment = s.Segment.ToString().ToLowerInvariant(),
            round = s.Round,
            // Not stored on the selection: a steal's position in its round is
            // arithmetic on the overall index, and a rookie's comes from the
            // entitlement it spent. Recovering it here keeps the board's
            // "S1.4" label the same label on both halves of the list.
            pickInRound = PickInRoundOf(ctx, s),
            byTeamName = ctx.TeamName(s.TeamId),
            fromTeamName = s.StolenFromTeamId is { } v ? ctx.TeamName(v) : null,
            done = true,
            passed = s.PlayerId is null,
            player = s.PlayerId is { } id && players.TryGetValue(id, out var p)
                ? new { playerId = id, shortName = p.Short, position = p.Position, positionGroup = p.Group }
                : null,
            madeUtc = (DateTime?)s.MadeUtc,
        };

    /// <summary>A turn nobody has taken yet — who is projected to hold it.</summary>
    private static object Upcoming(DraftContext ctx, DraftTurn t) => new
    {
        overallIndex = t.OverallIndex,
        segment = t.Segment.ToString().ToLowerInvariant(),
        round = t.Round,
        pickInRound = t.PickInRound,
        byTeamName = ctx.TeamName(t.TeamId),
        fromTeamName = (string?)null,
        done = false,
        passed = false,
        player = (object?)null,
        madeUtc = (DateTime?)null,
    };

    private static int PickInRoundOf(DraftContext ctx, DraftSelection s) =>
        s.Segment == DraftSegment.Steal
            ? DraftSegments.StealPickInRound(s.OverallIndex, ctx.OrderedTeamIds.Count)
            : ctx.Picks.FirstOrDefault(p => p.DraftPickId == s.DraftPickId)?.PickInRound ?? 0;

    private static object Row(DraftPoolRow r) => new
    {
        playerId = r.Candidate.PlayerId,
        shortName = r.ShortName,
        position = r.Position,
        positionGroup = r.Candidate.PositionGroup,
        capHit = r.CapHit,
        nhlTeam = r.NhlTeam,
        ownerTeamName = r.OwnerTeamName,
        ownerUsername = r.OwnerUsername,
    };

    // ---- helpers ----

    /// <summary>
    /// The scale to rank an ended season's points under: that season's own.
    ///
    /// Points belong to the rules they were earned under — the whole reason
    /// storing rules per season removes the "changing the scale restates the
    /// past" problem. A league in its very first off-season has no prior
    /// document at all, and the prepared season's scale is then the only one
    /// there is.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, double>> RankingScaleAsync(
        FantasyWarriorDbContext db, League league, string lastSeason, RuleSet prepared)
    {
        try
        {
            return (await RuleSetResolver.ForSeasonAsync(db, league.LeagueId, lastSeason))
                .Scoring.Values;
        }
        catch (RuleSetUnavailableException)
        {
            return prepared.Scoring.Values;
        }
    }

    private static async Task<string> ReasonAsync(
        FantasyWarriorDbContext db, DraftContext ctx, DraftTurn turn, long playerId)
    {
        var spot = await db.RosterSpots
            .AsNoTracking()
            .Where(s => s.LeagueId == ctx.League.LeagueId && s.PlayerId == playerId)
            .Where(RosterWindow.Committed())
            .Select(s => new { s.TeamId, s.ProtectionStatus })
            .FirstOrDefaultAsync();

        var player = await db.Players
            .AsNoTracking()
            .Where(p => p.PlayerId == playerId)
            .Select(p => new { p.PositionGroup, p.CareerNhlGames })
            .FirstOrDefaultAsync();

        if (player is null) return "No such player.";

        var candidate = new DraftCandidate(
            playerId, player.PositionGroup, player.CareerNhlGames,
            spot?.TeamId, spot?.ProtectionStatus == RosterProtectionStatus.Protected,
            spot is null ? 0 : ctx.LossesOf(spot.TeamId),
            ctx.TakenPlayerIds.Contains(playerId));

        return DraftPool.IneligibleReason(
            candidate, turn.Segment, turn.TeamId, ctx.MaxLossesPerTeam, ctx.AutoProtect)
            ?? "He is no longer available.";
    }

    private static async Task<long> CapOfAsync(FantasyWarriorDbContext db, int teamId) =>
        (await db.Standings.FirstOrDefaultAsync(s => s.TeamId == teamId))?.CapTotal ?? 0;

    private static Task<int> CountOfAsync(FantasyWarriorDbContext db, int teamId) =>
        db.RosterSpots
            .Where(s => s.TeamId == teamId && s.PlayerId != null)
            .Where(RosterWindow.Committed())
            .CountAsync();

    private static async Task<(League? League, IResult? Error)> CommissionerAsync(
        FantasyWarriorDbContext db, string leagueId, string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return (null, Results.BadRequest(new { error = "Username is required." }));

        var league = await Queries.LeagueByCodeAsync(db, leagueId);
        if (league is null) return (null, Results.NotFound(new { error = "League not found." }));

        var commissioner = await db.Users.FindAsync(league.CommissionerUserId);
        if (commissioner?.Username != Queries.Normalize(username))
            return (null, Results.Json(
                new { error = "Only the commissioner can run the draft." }, statusCode: 403));

        return (league, null);
    }

    /// <summary>
    /// Persist first, then push - the same order MessageEndpoints uses, and for
    /// the same reason: a push that raced ahead of the write would show a pick
    /// that a rollback then erased.
    ///
    /// The payload is the whole state rather than a delta. A client that just
    /// reconnected needs all of it anyway, applying it twice changes nothing,
    /// and a missed push is repaired by the next one - the same argument the hub
    /// already makes for presence.
    /// </summary>
    private static async Task PushAsync(
        IHubContext<LiveHub> hub, ILoggerFactory logs, League league, object? state, string text)
    {
        try
        {
            if (state is not null)
                await hub.Clients.Group(LiveHub.LeagueGroup(league.LeagueId)).SendAsync("draft", state);

            await hub.Clients.Group(LiveHub.LeagueGroup(league.LeagueId)).SendAsync(
                "notice", new { id = Guid.NewGuid().ToString("n"), kind = "draft", text });
        }
        catch (Exception ex)
        {
            // Never fail a pick over a push. The board is already correct in the
            // database and the next fetch repairs every screen.
            logs.CreateLogger(typeof(DraftEndpoints))
                .LogWarning(ex, "Could not push the draft update for {League}.", league.JoinCode);
        }
    }
}

/// <summary>
/// A pick. <c>PlayerId</c> is null to pass the turn, which a GM facing an empty
/// pool has to be able to do.
/// </summary>
/// <param name="ExpectedOverallIndex">
/// The turn the client believed it was acting on. Optional, but sending it is
/// what turns a stale tab into a clean 409 instead of a surprise.
/// </param>
public record DraftSelectRequest(string? Username, long? PlayerId, int? ExpectedOverallIndex);

public record DraftCommandRequest(string? Username);
