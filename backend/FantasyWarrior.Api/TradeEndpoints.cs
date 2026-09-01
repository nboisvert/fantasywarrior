using FantasyWarrior.Core.Cockcoin;
using FantasyWarrior.Core.Periods;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Core.Trades;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Rosters;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

public static class TradeEndpoints
{
    /// <summary>
    /// The wire spelling of a status. The column is a tinyint enum now; the
    /// frontend has always read these strings.
    /// </summary>
    private static string Wire(TradeStatus s) => s switch
    {
        TradeStatus.Pending => "pending",
        TradeStatus.Declined => "declined",
        TradeStatus.Cancelled => "cancelled",
        TradeStatus.Accepted => "accepted",
        _ => "processed",
    };

    /// <summary>
    /// Runs the cap and roster checks for both teams against their *engaged*
    /// figures. Shared by propose and accept so the two can never drift into
    /// deciding differently — the whole point of re-checking on accept is that
    /// it asks the same question of newer facts.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ValidateAgainstEngagedAsync(
        FantasyWarriorDbContext db, League league, int proposerTeamId, int counterpartyTeamId,
        IReadOnlyCollection<long> fromProposer, IReadOnlyCollection<long> fromCounterparty,
        bool includesPicks)
    {
        // A trade closes a spot and opens a new one, and the new one inherits
        // no protection at all — a player a GM had just protected would
        // silently become stealable. Freezing trades for exactly the two
        // phases where that could happen (offseason.md) closes the
        // hole rather than working around it after the fact.
        var activeSeason = await Queries.ActiveLeagueSeasonAsync(db, league.LeagueId);
        if (activeSeason is null)
            return ["This league has no open season, so nothing says what a legal roster is."];
        if (!SeasonPhaseRules.CanTrade(activeSeason.Phase))
            return [$"Trades are frozen during the off-season {activeSeason.Phase.ToString().ToLowerInvariant()} phase."];
        if (activeSeason.Rules.IsUnwritten)
            return ["This league's rules were never written. Run `rules-backfill`."];

        // The **open** season's rules, not the scored one's. They are the same
        // row all through InSeason; in PreSeason they differ, and a trade then
        // is repairing a roster for the season about to start — so the bounds
        // that matter are the ones it will be judged by.
        var rules = activeSeason.Rules;

        if (!rules.Trades.Enabled) return ["This league does not allow trades."];
        if (includesPicks && !rules.Trades.PicksTradable)
            return ["This league does not allow draft picks to be traded."];

        var engaged = await TradeValidation.EngagedStateAsync(db, league.LeagueId);
        if (!engaged.TryGetValue(proposerTeamId, out var proposerState)
            || !engaged.TryGetValue(counterpartyTeamId, out var counterpartyState))
            return [];

        var capHits = await Queries.CapHitsAsync(
            db, league.Season, [.. fromProposer.Concat(fromCounterparty)]);

        return TradeValidation.Validate(
            rules, proposerState, counterpartyState, fromProposer, fromCounterparty, capHits);
    }

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/leagues/{leagueId}/trades", async (
            string leagueId, ProposeTradeRequest req, FantasyWarriorDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.CounterpartyUsername))
                return Results.BadRequest(new { error = "Username and counterpartyUsername are required." });

            var proposer = Queries.Normalize(req.Username);
            var counterparty = Queries.Normalize(req.CounterpartyUsername);
            if (proposer == counterparty)
                return Results.BadRequest(new { error = "Cannot trade with yourself." });

            var fromProposer = req.PlayersFromProposer ?? [];
            var fromCounterparty = req.PlayersFromCounterparty ?? [];
            var franchiseFromProposer = Blank(req.FranchiseFromProposer);
            var franchiseFromCounterparty = Blank(req.FranchiseFromCounterparty);
            if (fromProposer.Count == 0 && fromCounterparty.Count == 0
                && (req.PicksFromProposer ?? []).Count == 0 && (req.PicksFromCounterparty ?? []).Count == 0
                && franchiseFromProposer is null && franchiseFromCounterparty is null)
                return Results.BadRequest(new { error = "Trade must include at least one player, pick or franchise." });
            if (fromProposer.Intersect(fromCounterparty).Any())
                return Results.BadRequest(new { error = "A player can't appear on both sides of a trade." });

            // A franchise only ever moves against another franchise: every team
            // holds exactly one, so a one-sided swap would leave one team with
            // two and the other with none — refused by the database in the
            // middle of the nightly job rather than here, where the GM can
            // still be told.
            var balanceErrors = TradeRules.ValidateFranchiseBalance(
                franchiseFromProposer is null ? 0 : 1,
                franchiseFromCounterparty is null ? 0 : 1);
            if (balanceErrors.Count > 0)
                return Results.BadRequest(new { error = string.Join(" ", balanceErrors), errors = balanceErrors });

            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var proposerTeam = await Queries.TeamAsync(db, league.LeagueId, proposer);
            var counterpartyTeam = await Queries.TeamAsync(db, league.LeagueId, counterparty);
            if (proposerTeam is null || counterpartyTeam is null)
                return Results.NotFound(new { error = "Team not found." });

            // Ownership is checked against open roster spots, which is the only
            // record of who holds whom — there is no denormalised player list to
            // fall out of step with it any more.
            var held = await db.RosterSpots
                .Where(s => s.LeagueId == league.LeagueId && s.EndDate == null)
                .Select(s => new { s.TeamId, s.PlayerId, s.FranchiseAbbrev })
                .ToListAsync();
            var proposerHas = held.Where(h => h.TeamId == proposerTeam.TeamId && h.PlayerId != null)
                .Select(h => h.PlayerId!.Value).ToHashSet();
            var counterpartyHas = held.Where(h => h.TeamId == counterpartyTeam.TeamId && h.PlayerId != null)
                .Select(h => h.PlayerId!.Value).ToHashSet();

            if (fromProposer.Any(id => !proposerHas.Contains(id)))
                return Results.BadRequest(new { error = "You can only offer players on your own roster." });
            if (fromCounterparty.Any(id => !counterpartyHas.Contains(id)))
                return Results.BadRequest(new { error = "Requested players aren't on that team's roster." });

            // The franchise offered has to be the one actually held, not the
            // one a stale screen still shows — the Équipe slot moves, and the
            // team's own name (Teams.FranchiseAbbrev) does not follow it.
            string? FranchiseOf(int teamId) =>
                held.FirstOrDefault(h => h.TeamId == teamId && h.FranchiseAbbrev != null)?.FranchiseAbbrev;

            if (franchiseFromProposer is not null && FranchiseOf(proposerTeam.TeamId) != franchiseFromProposer)
                return Results.BadRequest(new { error = "You can only offer the franchise you hold." });
            if (franchiseFromCounterparty is not null
                && FranchiseOf(counterpartyTeam.TeamId) != franchiseFromCounterparty)
                return Results.BadRequest(new { error = "That team doesn't hold the franchise you asked for." });

            // Draft picks. Held, unused, and of the one tradable year — picks
            // only ever exist one season ahead, so anything else is a stale id.
            var picksFromProposer = req.PicksFromProposer ?? [];
            var picksFromCounterparty = req.PicksFromCounterparty ?? [];
            var pickIds = picksFromProposer.Concat(picksFromCounterparty).ToList();
            var picks = pickIds.Count == 0
                ? []
                : await db.DraftPicks.Where(p => p.LeagueId == league.LeagueId && pickIds.Contains(p.DraftPickId))
                    .ToListAsync();

            foreach (var (ids, teamId, whose) in new[]
                     {
                         (picksFromProposer, proposerTeam.TeamId, "your own"),
                         (picksFromCounterparty, counterpartyTeam.TeamId, "that team's"),
                     })
                foreach (var id in ids)
                {
                    var pick = picks.FirstOrDefault(p => p.DraftPickId == id);
                    if (pick is null || pick.CurrentTeamId != teamId)
                        return Results.BadRequest(new { error = $"You can only trade {whose} draft picks." });
                    if (pick.UsedUtc is not null || pick.PlayerId is not null)
                        return Results.BadRequest(new { error = "That pick has already been used." });
                }

            // Nothing already promised away by an accepted trade. Without this
            // the same player can be sold twice between the acceptance and the
            // nightly job, and the second trade explodes at 09:30 UTC instead
            // of here.
            var engagedAssets = await TradeValidation.EngagedAssetsAsync(db, league.LeagueId);
            if (fromProposer.Concat(fromCounterparty).Any(engagedAssets.PlayerIds.Contains))
                return Results.BadRequest(
                    new { error = "A player in this offer is already committed in an accepted trade." });
            if (pickIds.Any(engagedAssets.DraftPickIds.Contains))
                return Results.BadRequest(
                    new { error = "A pick in this offer is already committed in an accepted trade." });
            if (new[] { franchiseFromProposer, franchiseFromCounterparty }
                    .Any(a => a is not null && engagedAssets.FranchiseAbbrevs.Contains(a)))
                return Results.BadRequest(
                    new { error = "A franchise in this offer is already committed in an accepted trade." });

            var capErrors = await ValidateAgainstEngagedAsync(
                db, league, proposerTeam.TeamId, counterpartyTeam.TeamId, fromProposer, fromCounterparty,
                includesPicks: picksFromProposer.Count > 0 || picksFromCounterparty.Count > 0);
            if (capErrors.Count > 0)
                return Results.BadRequest(new { error = string.Join(" ", capErrors), errors = capErrors });

            var trade = new Trade
            {
                LeagueId = league.LeagueId,
                ProposerTeamId = proposerTeam.TeamId,
                CounterpartyTeamId = counterpartyTeam.TeamId,
                Status = TradeStatus.Pending,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Trades.Add(trade);
            await db.SaveChangesAsync();

            // The Nth offer from this proposer to this counterparty — a
            // milestone count landing on a Fibonacci number earns the
            // proposer cockcoin. Rides the handler's own later SaveChanges.
            var offerCount = await db.Trades.CountAsync(x =>
                x.LeagueId == league.LeagueId && x.ProposerTeamId == proposerTeam.TeamId
                && x.CounterpartyTeamId == counterpartyTeam.TeamId);
            int? milestoneAmount = FibonacciMilestones.RewardForCount(offerCount);
            if (milestoneAmount is not null)
                db.CockcoinAwards.Add(new CockcoinAward
                {
                    UserId = proposerTeam.OwnerUserId,
                    Amount = milestoneAmount.Value,
                    Reason = CockcoinReasons.TradeOfferMilestone,
                    AwardedUtc = DateTime.UtcNow,
                });

            foreach (var playerId in fromProposer)
                db.TradeAssets.Add(new TradeAsset
                {
                    TradeId = trade.TradeId, FromTeamId = proposerTeam.TeamId, ToTeamId = counterpartyTeam.TeamId,
                    AssetType = TradeAssetType.Player, PlayerId = playerId,
                });
            foreach (var playerId in fromCounterparty)
                db.TradeAssets.Add(new TradeAsset
                {
                    TradeId = trade.TradeId, FromTeamId = counterpartyTeam.TeamId, ToTeamId = proposerTeam.TeamId,
                    AssetType = TradeAssetType.Player, PlayerId = playerId,
                });
            foreach (var pickId in picksFromProposer)
                db.TradeAssets.Add(new TradeAsset
                {
                    TradeId = trade.TradeId, FromTeamId = proposerTeam.TeamId, ToTeamId = counterpartyTeam.TeamId,
                    AssetType = TradeAssetType.DraftPick, DraftPickId = pickId,
                });
            foreach (var pickId in picksFromCounterparty)
                db.TradeAssets.Add(new TradeAsset
                {
                    TradeId = trade.TradeId, FromTeamId = counterpartyTeam.TeamId, ToTeamId = proposerTeam.TeamId,
                    AssetType = TradeAssetType.DraftPick, DraftPickId = pickId,
                });
            if (franchiseFromProposer is not null)
                db.TradeAssets.Add(new TradeAsset
                {
                    TradeId = trade.TradeId, FromTeamId = proposerTeam.TeamId, ToTeamId = counterpartyTeam.TeamId,
                    AssetType = TradeAssetType.Franchise, FranchiseAbbrev = franchiseFromProposer,
                });
            if (franchiseFromCounterparty is not null)
                db.TradeAssets.Add(new TradeAsset
                {
                    TradeId = trade.TradeId, FromTeamId = counterpartyTeam.TeamId, ToTeamId = proposerTeam.TeamId,
                    AssetType = TradeAssetType.Franchise, FranchiseAbbrev = franchiseFromCounterparty,
                });
            await db.SaveChangesAsync();

            return Results.Ok(new { id = trade.TradeId.ToString(), cockcoinAwarded = milestoneAmount ?? 0 });
        });

        // What a team holds. Picks only ever exist one season ahead, so there
        // is no year to ask for — whatever exists is what is tradable.
        app.MapGet("/api/leagues/{leagueId}/teams/{username}/picks", async (
            string leagueId, string username, FantasyWarriorDbContext db) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });

            var team = await Queries.TeamAsync(db, league.LeagueId, Queries.Normalize(username));
            if (team is null) return Results.NotFound(new { error = "Team not found." });

            var engaged = (await TradeValidation.EngagedAssetsAsync(db, league.LeagueId)).DraftPickIds;

            var picks = await db.DraftPicks
                .Where(p => p.CurrentTeamId == team.TeamId && p.UsedUtc == null && p.PlayerId == null)
                .OrderBy(p => p.Year).ThenBy(p => p.Round)
                .Select(p => new
                {
                    p.DraftPickId,
                    p.Year,
                    p.Round,
                    // "Pittsburgh's 2nd, via Boston" is expressible only because
                    // the original owner is never overwritten by a trade.
                    originalTeamName = p.OriginalTeam!.Name,
                    viaTrade = p.OriginalTeamId != p.CurrentTeamId,
                })
                .ToListAsync();

            return Results.Ok(picks.Select(p => new
            {
                id = p.DraftPickId,
                p.Year,
                p.Round,
                p.originalTeamName,
                p.viaTrade,
                engaged = engaged.Contains(p.DraftPickId),
            }));
        });

        app.MapPost("/api/leagues/{leagueId}/trades/{tradeId}/respond", async (
            string leagueId, string tradeId, RespondTradeRequest req,
            FantasyWarriorDbContext db, SimulationClockService clock) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { error = "Username is required." });
            if (!int.TryParse(tradeId, out var id))
                return Results.NotFound(new { error = "Trade not found." });

            var trade = await db.Trades
                .Include(t => t.ProposerTeam).ThenInclude(t => t!.Owner)
                .Include(t => t.CounterpartyTeam).ThenInclude(t => t!.Owner)
                .FirstOrDefaultAsync(t => t.TradeId == id);
            if (trade is null) return Results.NotFound(new { error = "Trade not found." });

            var username = Queries.Normalize(req.Username);
            var isProposer = trade.ProposerTeam!.Owner!.Username == username;
            var isCounterparty = trade.CounterpartyTeam!.Owner!.Username == username;
            var now = DateTime.UtcNow;

            if (req.Accept)
            {
                if (trade.Status != TradeStatus.Pending || !isCounterparty)
                    return Results.Json(new { error = "Only the receiving team can accept this trade." }, statusCode: 403);

                // Re-check, because accepting is the last moment anyone can be
                // told. Rosters move between proposing and answering: another
                // trade executes, or one of these players gets promised
                // elsewhere and accepted first.
                var league = await db.Leagues.FirstAsync(l => l.LeagueId == trade.LeagueId);
                var assets = await db.TradeAssets.Where(a => a.TradeId == trade.TradeId).ToListAsync();

                var offered = assets
                    .Where(a => a.AssetType == TradeAssetType.Player && a.FromTeamId == trade.ProposerTeamId)
                    .Select(a => a.PlayerId!.Value).ToList();
                var requested = assets
                    .Where(a => a.AssetType == TradeAssetType.Player && a.FromTeamId == trade.CounterpartyTeamId)
                    .Select(a => a.PlayerId!.Value).ToList();

                // This trade's own assets are not yet engaged — it is still
                // pending — so anything flagged here belongs to a *different*
                // accepted trade.
                var engagedAssets = await TradeValidation.EngagedAssetsAsync(db, trade.LeagueId);
                if (offered.Concat(requested).Any(engagedAssets.PlayerIds.Contains))
                    return Results.BadRequest(new
                    {
                        error = "A player in this offer has since been committed in another accepted trade.",
                    });
                if (assets.Where(a => a.AssetType == TradeAssetType.DraftPick)
                        .Any(a => engagedAssets.DraftPickIds.Contains(a.DraftPickId!.Value)))
                    return Results.BadRequest(new
                    {
                        error = "A pick in this offer has since been committed in another accepted trade.",
                    });
                if (assets.Where(a => a.AssetType == TradeAssetType.Franchise)
                        .Any(a => engagedAssets.FranchiseAbbrevs.Contains(a.FranchiseAbbrev!)))
                    return Results.BadRequest(new
                    {
                        error = "A franchise in this offer has since been committed in another accepted trade.",
                    });

                var errors = await ValidateAgainstEngagedAsync(
                    db, league, trade.ProposerTeamId, trade.CounterpartyTeamId, offered, requested,
                    includesPicks: trade.Assets.Any(a => a.AssetType == TradeAssetType.DraftPick));
                if (errors.Count > 0)
                    return Results.BadRequest(new { error = string.Join(" ", errors), errors });

                // **Where the swap actually lands**, decided here rather than by
                // whichever night the job happens to catch it. Everything the
                // app says about this trade from now on — who is on whose
                // roster, whose lineup he can be put in, what each cap is
                // committed to — is read off the dates stamped below.
                var periods = await db.Periods
                    .Where(p => p.Season == league.Season)
                    .ToListAsync();
                var effectiveDate = TradeSchedule.NextPeriodStart(
                    periods.Select(p => new PeriodSpan(p.Number, p.StartDate, p.EndDate)),
                    await clock.TodayEtAsync());
                if (effectiveDate is not { } effective)
                    return Results.Conflict(new
                    {
                        error = "The season has no week left for this trade to take effect in.",
                    });
                var nextPeriod = periods.First(p => p.StartDate == effective);

                await db.Entry(trade).Collection(t => t.Assets).LoadAsync();

                trade.Status = TradeStatus.Accepted;
                trade.RespondedUtc = now;
                trade.EffectiveDate = effective;

                // One transaction over the validation above and the roster
                // change below: a half-applied swap is the one outcome that
                // leaves the league in a state no rule can describe. Through
                // the execution strategy because retries are enabled — the
                // serverless tier drops connections on resume, and a retry must
                // replay the whole swap rather than half of it.
                var strategy = db.Database.CreateExecutionStrategy();
                int? acceptMilestoneAmount = null;
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    await TradeExecution.ApplyAsync(db, trade, effective, nextPeriod);
                    await db.SaveChangesAsync();

                    // The Nth trade this counterparty has accepted from this
                    // specific proposer — the symmetric peer of the propose-side
                    // milestone, earned by the acceptor instead. Counted after
                    // the save above so this trade's own new Accepted row is
                    // already on the table.
                    var acceptCount = await db.Trades.CountAsync(x =>
                        x.LeagueId == trade.LeagueId && x.ProposerTeamId == trade.ProposerTeamId
                        && x.CounterpartyTeamId == trade.CounterpartyTeamId
                        && (x.Status == TradeStatus.Accepted || x.Status == TradeStatus.Processed));
                    acceptMilestoneAmount = FibonacciMilestones.RewardForCount(acceptCount);
                    if (acceptMilestoneAmount is not null)
                    {
                        db.CockcoinAwards.Add(new CockcoinAward
                        {
                            UserId = trade.CounterpartyTeam!.Owner!.UserId,
                            Amount = acceptMilestoneAmount.Value,
                            Reason = CockcoinReasons.TradeOfferAccepted,
                            AwardedUtc = DateTime.UtcNow,
                        });
                        await db.SaveChangesAsync();
                    }

                    await tx.CommitAsync();
                });

                return Results.Ok(new
                {
                    ok = true, status = "accepted",
                    effectiveDate = effective,
                    note = $"Rosters change on {effective:MMMM d}, when week {nextPeriod.Number} opens.",
                    cockcoinAwarded = acceptMilestoneAmount ?? 0,
                });
            }

            // Declining branches by who is acting: the proposer withdrawing his
            // own offer is "cancelled", the counterparty rejecting it is
            // "declined" — distinct statuses so the status alone says who acted.
            if (isProposer)
            {
                if (trade.Status != TradeStatus.Pending)
                    return Results.Json(new { error = "This offer can no longer be cancelled." }, statusCode: 403);
                trade.Status = TradeStatus.Cancelled;
                trade.RespondedUtc = now;
                await db.SaveChangesAsync();
                return Results.Ok(new { ok = true, status = "cancelled" });
            }

            if (trade.Status != TradeStatus.Pending || !isCounterparty)
                return Results.Json(new { error = "Only the receiving team can decline this trade." }, statusCode: 403);
            trade.Status = TradeStatus.Declined;
            trade.RespondedUtc = now;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, status = "declined" });
        });

        app.MapGet("/api/leagues/{leagueId}/trades", async (
            string leagueId, string? username, FantasyWarriorDbContext db) =>
        {
            var league = await Queries.LeagueByCodeAsync(db, leagueId);
            if (league is null) return Results.NotFound(new { error = "League not found." });
            if (string.IsNullOrWhiteSpace(username))
                return Results.BadRequest(new { error = "username is required." });
            var viewer = Queries.Normalize(username);

            var trades = await db.Trades
                .Where(t => t.LeagueId == league.LeagueId)
                .Include(t => t.Assets)
                .Include(t => t.ProposerTeam).ThenInclude(t => t!.Owner)
                .Include(t => t.CounterpartyTeam).ThenInclude(t => t!.Owner)
                .Include(t => t.Votes).ThenInclude(v => v.User)
                .OrderByDescending(t => t.CreatedUtc)
                .ToListAsync();

            // pending/declined/cancelled are private to the two parties;
            // accepted/processed are public to the whole league.
            trades = trades.Where(t =>
                t.Status is TradeStatus.Accepted or TradeStatus.Processed
                || t.ProposerTeam!.Owner!.Username == viewer
                || t.CounterpartyTeam!.Owner!.Username == viewer).ToList();

            var allPlayerIds = trades.SelectMany(t => t.Assets)
                .Where(a => a.PlayerId is not null).Select(a => a.PlayerId!.Value).Distinct().ToList();
            var players = await db.Players.Where(p => allPlayerIds.Contains(p.PlayerId))
                .ToDictionaryAsync(p => p.PlayerId);

            var allAbbrevs = trades.SelectMany(t => t.Assets)
                .Where(a => a.FranchiseAbbrev is not null).Select(a => a.FranchiseAbbrev!).Distinct().ToList();
            var franchises = await db.NhlTeams.Where(t => allAbbrevs.Contains(t.Abbrev))
                .ToDictionaryAsync(t => t.Abbrev);

            var allPickIds = trades.SelectMany(t => t.Assets)
                .Where(a => a.DraftPickId is not null).Select(a => a.DraftPickId!.Value).Distinct().ToList();
            var picks = await db.DraftPicks
                .Where(p => allPickIds.Contains(p.DraftPickId))
                .Select(p => new
                {
                    p.DraftPickId,
                    p.Year,
                    p.Round,
                    p.OriginalTeamId,
                    OriginalTeamName = p.OriginalTeam!.Name,
                })
                .ToDictionaryAsync(p => p.DraftPickId);

            // **Everything that changed hands**, in one list, because a trade
            // recap has to read as one sentence: "Crosby, a 1st and the
            // Canadiens for Makar and the Avalanche". Picks were missing here
            // from the day they became tradable (2026-08-03) — the offer was
            // written correctly and simply came back describing nothing, so a
            // picks-only trade rendered as "nothing for nothing".
            //
            // Non-player assets carry a negative id, which cannot collide with
            // a player's in a row key, and a position the screens already read:
            // "T" for a franchise, "P" for a pick.
            object[] Side(Trade t, int fromTeamId) =>
            [
                .. t.Assets
                    .Where(a => a.FromTeamId == fromTeamId && a.AssetType == TradeAssetType.Player)
                    .Select(a =>
                    {
                        players.TryGetValue(a.PlayerId!.Value, out var p);
                        return (object)new
                        {
                            id = a.PlayerId!.Value,
                            name = p?.FullName ?? "Unknown player",
                            position = p?.Position,
                        };
                    }),
                .. t.Assets
                    .Where(a => a.FromTeamId == fromTeamId && a.AssetType == TradeAssetType.DraftPick)
                    .Select(a =>
                    {
                        var pick = picks.GetValueOrDefault(a.DraftPickId!.Value);
                        // "2026 rd 1", matching the trade sheet's own wording,
                        // plus whose pick it is when it is not the sender's —
                        // "Pittsburgh's 2nd, via Boston" is the whole reason
                        // OriginalTeamId survives every trade.
                        var label = pick is null
                            ? "Draft pick"
                            : $"{pick.Year} rd {pick.Round}"
                              + (pick.OriginalTeamId == fromTeamId ? "" : $" · {pick.OriginalTeamName}");
                        return (object)new
                        {
                            id = -(long)a.TradeAssetId,
                            name = label,
                            position = "P",
                        };
                    }),
                .. t.Assets
                    .Where(a => a.FromTeamId == fromTeamId && a.AssetType == TradeAssetType.Franchise)
                    .Select(a => (object)new
                    {
                        id = -(long)a.TradeAssetId,
                        name = franchises.GetValueOrDefault(a.FranchiseAbbrev!)?.Name ?? a.FranchiseAbbrev!,
                        position = "T",
                    }),
            ];

            return Results.Ok(trades.Select(t =>
            {
                var votes = t.Status == TradeStatus.Processed ? t.Votes.ToList() : [];
                var mine = votes.FirstOrDefault(v => v.User!.Username == viewer);
                // Either party has a stake in the outcome, so they don't get a
                // vote — but they still deserve to see how their own trade was
                // judged rather than being permanently blind to it (the "vote to
                // reveal" rule exists to protect voters from bias, and a party
                // isn't voting at all).
                var isParty = viewer == t.ProposerTeam!.Owner!.Username || viewer == t.CounterpartyTeam!.Owner!.Username;
                return new
                {
                    id = t.TradeId.ToString(),
                    proposerUsername = t.ProposerTeam!.Owner!.Username,
                    proposerTeamName = t.ProposerTeam.Name,
                    counterpartyUsername = t.CounterpartyTeam!.Owner!.Username,
                    counterpartyTeamName = t.CounterpartyTeam.Name,
                    playersFromProposer = Side(t, t.ProposerTeamId),
                    playersFromCounterparty = Side(t, t.CounterpartyTeamId),
                    status = Wire(t.Status),
                    createdUtc = t.CreatedUtc,
                    respondedUtc = t.RespondedUtc,
                    processedUtc = t.ProcessedUtc,
                    // Withheld until the viewer has voted themselves — not just
                    // hidden in the UI, since the point is nobody's opinion gets
                    // to lean on the crowd's before they've formed their own
                    // (2026-08-02, per Nick) — except a party, who can never
                    // vote and so is never made to wait.
                    votes = mine is null && !isParty ? null : new
                    {
                        proposer = votes.Count(v => v.FavoredTeamId == t.ProposerTeamId),
                        fair = votes.Count(v => v.FavoredTeamId is null),
                        counterparty = votes.Count(v => v.FavoredTeamId == t.CounterpartyTeamId),
                        total = votes.Count,
                    },
                    myVote = mine is null ? null : new
                    {
                        favoredUsername = mine.FavoredTeamId == t.ProposerTeamId ? t.ProposerTeam.Owner.Username
                            : mine.FavoredTeamId == t.CounterpartyTeamId ? t.CounterpartyTeam.Owner.Username
                            : null,
                    },
                    // A party to the trade can't vote on it — self-interest.
                    canVote = t.Status == TradeStatus.Processed && !isParty,
                };
            }));
        });

        app.MapPost("/api/leagues/{leagueId}/trades/{tradeId}/vote", async (
            string leagueId, string tradeId, VoteTradeRequest req, FantasyWarriorDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { error = "Username is required." });
            if (!int.TryParse(tradeId, out var id))
                return Results.NotFound(new { error = "Trade not found." });

            var trade = await db.Trades
                .Include(t => t.ProposerTeam).ThenInclude(t => t!.Owner)
                .Include(t => t.CounterpartyTeam).ThenInclude(t => t!.Owner)
                .FirstOrDefaultAsync(t => t.TradeId == id);
            if (trade is null) return Results.NotFound(new { error = "Trade not found." });
            if (trade.Status != TradeStatus.Processed)
                return Results.BadRequest(new { error = "Only processed trades can be rated." });

            // The frontend still speaks in usernames; the column is a team id,
            // which is what lets a future "who wins their trades" rollup filter
            // across every trade without knowing who proposed which.
            int? favoredTeamId =
                req.FavoredUsername is null ? null
                : Queries.Normalize(req.FavoredUsername) == trade.ProposerTeam!.Owner!.Username ? trade.ProposerTeamId
                : Queries.Normalize(req.FavoredUsername) == trade.CounterpartyTeam!.Owner!.Username ? trade.CounterpartyTeamId
                : -1;

            if (favoredTeamId == -1)
                return Results.BadRequest(new
                {
                    error = "Invalid vote: favoredUsername must be null (fair) or one of the two teams.",
                });

            var username = Queries.Normalize(req.Username);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null) return Results.NotFound(new { error = "User not found." });

            // Either party has a stake in the outcome, so neither gets a vote —
            // self-interest, same reasoning as a trade being invisible to
            // outsiders while it's still pending.
            if (username == trade.ProposerTeam!.Owner!.Username || username == trade.CounterpartyTeam!.Owner!.Username)
                return Results.BadRequest(new { error = "You can't vote on your own trade." });

            // Votes are permanent (2026-08-03, per Nick) — the frontend confirms
            // before ever sending this, but the server is the one that actually
            // enforces it, same as every other rule here.
            var alreadyVoted = await db.TradeVotes.AnyAsync(v => v.TradeId == id && v.UserId == user.UserId);
            if (alreadyVoted)
                return Results.BadRequest(new { error = "You've already voted on this trade — votes can't be changed." });

            db.TradeVotes.Add(new TradeVote
            {
                TradeId = id, UserId = user.UserId, FavoredTeamId = favoredTeamId, VotedUtc = DateTime.UtcNow,
            });

            // Casting a vote earns cockcoin — same save as the vote itself,
            // not a separate round-trip, so the two can never disagree about
            // whether the vote actually went through.
            db.CockcoinAwards.Add(new CockcoinAward
            {
                UserId = user.UserId,
                Amount = CockcoinReasons.TradeVoteAmount,
                Reason = CockcoinReasons.TradeVote,
                AwardedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            var cockcoinBalance = await db.CockcoinBalances
                .Where(b => b.UserId == user.UserId)
                .Select(b => (int?)b.Balance)
                .FirstOrDefaultAsync() ?? 0;

            return Results.Ok(new
            {
                ok = true,
                cockcoinAwarded = CockcoinReasons.TradeVoteAmount,
                cockcoinBalance,
            });
        });
    }

    /// <summary>
    /// Whitespace and the empty string both mean "no franchise in this offer" —
    /// a client builds the field from a picker that may hand back either.
    /// </summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

public record ProposeTradeRequest(
    string? Username, string? CounterpartyUsername,
    List<long>? PlayersFromProposer, List<long>? PlayersFromCounterparty,
    List<int>? PicksFromProposer = null, List<int>? PicksFromCounterparty = null,
    // The Équipe slot. An abbrev rather than a flag so the server can refuse an
    // offer built on a stale screen — the franchise a team holds moves, and its
    // own name does not follow it.
    string? FranchiseFromProposer = null, string? FranchiseFromCounterparty = null);
public record RespondTradeRequest(string? Username, bool Accept);
public record VoteTradeRequest(string? Username, string? FavoredUsername);
