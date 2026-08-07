using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// The four views, which between them define every number the app shows above
/// the per-week grain.
///
/// This is where the logic that used to live in C# now lives, so it needs the
/// same standard of coverage the scoring engine has. The invariant worth
/// staring at is in <see cref="Standings_ScoreIsAlwaysFinalizedPlusLive"/>:
/// under Firestore that was three stored fields kept in agreement by hand.
/// </summary>
[Collection(SqlCollection.Name)]
public class ViewTests
{
    [SqlFact]
    public async Task PlayerSeasonStats_SumsTheGameLog_AndExcludesPlayoffs()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var player = await world.AddPlayerAsync();

        await world.AddGameLineAsync(player, new DateOnly(2025, 10, 8), goals: 2, assists: 1);
        await world.AddGameLineAsync(player, new DateOnly(2025, 10, 10), goals: 0, assists: 3);
        // Playoffs never count toward a pool score. This is a rule, not an
        // oversight — scoring-model.md §4.
        await world.AddGameLineAsync(player, new DateOnly(2026, 5, 1),
            gameType: Entities.GameType.Playoffs, goals: 5, assists: 5);

        var totals = await db.PlayerSeasonStats.SingleAsync(s => s.PlayerId == player.PlayerId);

        Assert.Equal(2, totals.GamesPlayed);
        Assert.Equal(2, totals.Goals);
        Assert.Equal(4, totals.Assists);
        Assert.Equal(6, totals.Points);
    }

    [SqlFact]
    public async Task PlayerSeasonStats_DerivesGoalieCountsFromTheDecisionFlags()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var goalie = await world.AddPlayerAsync("G");

        await world.AddGameLineAsync(goalie, new DateOnly(2025, 10, 8), decision: "W", shutout: true);
        await world.AddGameLineAsync(goalie, new DateOnly(2025, 10, 10), decision: "W");
        await world.AddGameLineAsync(goalie, new DateOnly(2025, 10, 12), decision: "O", otLoss: true);
        await world.AddGameLineAsync(goalie, new DateOnly(2025, 10, 14), decision: "L");

        var totals = await db.PlayerSeasonStats.SingleAsync(s => s.PlayerId == goalie.PlayerId);

        Assert.Equal(4, totals.GamesPlayed);
        Assert.Equal(2, totals.Wins);
        Assert.Equal(1, totals.OtLosses);
        Assert.Equal(1, totals.Shutouts);
    }

    [SqlFact]
    public async Task PlayerSeasonStats_SeparatesSeasons()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var player = await world.AddPlayerAsync();

        await world.AddGameLineAsync(player, new DateOnly(2025, 10, 8), goals: 3);
        await world.AddGameLineAsync(player, new DateOnly(2024, 10, 8), goals: 7, season: "20242025");

        var rows = await db.PlayerSeasonStats.Where(s => s.PlayerId == player.PlayerId).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows.Single(r => r.Season == "20252026").Goals);
        Assert.Equal(7, rows.Single(r => r.Season == "20242025").Goals);
    }

    [SqlFact]
    public async Task RosterSpotTotals_SplitActiveFromBench()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(periods: 3);
        var spot = await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync());

        await world.AddAssignmentAsync(spot, world.Periods[0], active: true, points: 6, goals: 3, assists: 3, finalized: true);
        await world.AddAssignmentAsync(spot, world.Periods[1], active: false, points: 9, goals: 5, assists: 4);
        await world.AddAssignmentAsync(spot, world.Periods[2], active: true, points: 4, goals: 2, assists: 2);

        var totals = await db.RosterSpotTotals.SingleAsync(t => t.RosterSpotId == spot.RosterSpotId);

        Assert.Equal(10, totals.ActivePoints);
        // The week he sat is the "points left on the bench" number, and it must
        // never leak into the active total.
        Assert.Equal(9, totals.BenchPoints);
        Assert.Equal(6, totals.FinalizedActivePoints);
        // Counting stats follow the same split: only what he did while playing
        // for this team counts for it.
        Assert.Equal(5, totals.ActiveGoals);
        Assert.Equal(5, totals.ActiveAssists);
        Assert.Equal(2, totals.ActiveGamesPlayed);
    }

    [SqlFact]
    public async Task RosterSpotTotals_AreZero_NotMissing_ForASpotThatHasNeverScored()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        // A player just acquired: a real roster row with nothing behind it yet.
        var spot = await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync());

        var totals = await db.RosterSpotTotals.SingleAsync(t => t.RosterSpotId == spot.RosterSpotId);

        Assert.Equal(0, totals.ActivePoints);
        Assert.Equal(0, totals.ActiveGamesPlayed);
    }

    [SqlFact]
    public async Task TeamPeriodScores_GiveOneRowPerTeamPerWeek()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(periods: 2);

        var a = await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync());
        var b = await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync("D"));

        await world.AddAssignmentAsync(a, world.Periods[0], active: true, points: 5);
        await world.AddAssignmentAsync(b, world.Periods[0], active: false, points: 3);
        await world.AddAssignmentAsync(a, world.Periods[1], active: true, points: 7);
        await world.AddAssignmentAsync(b, world.Periods[1], active: true, points: 2);

        var weeks = await db.TeamPeriodScores
            .Where(s => s.TeamId == world.Teams[0].TeamId)
            .OrderBy(s => s.PeriodNumber)
            .ToListAsync();

        Assert.Equal(2, weeks.Count);
        Assert.Equal(5, weeks[0].ActivePoints);
        Assert.Equal(3, weeks[0].BenchPoints);
        Assert.Equal(9, weeks[1].ActivePoints);
        Assert.Equal(0, weeks[1].BenchPoints);
    }

    [SqlFact]
    public async Task Standings_ScoreIsAlwaysFinalizedPlusLive()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(periods: 2);
        var spot = await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync());

        // Week 1 is banked and can never move again; week 2 is still in progress.
        await world.AddAssignmentAsync(spot, world.Periods[0], active: true, points: 12, finalized: true);
        await world.AddAssignmentAsync(spot, world.Periods[1], active: true, points: 5);

        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        Assert.Equal(17, row.Score);
        Assert.Equal(12, row.FinalizedScore);
        Assert.Equal(5, row.LivePoints);
        // The invariant, held by construction because both sides come from the
        // same statement over the same rows.
        Assert.Equal(row.Score, row.FinalizedScore + row.LivePoints);
    }

    [SqlFact]
    public async Task Standings_KeepPointsFromAPlayerWhoHasLeftTheRoster()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(periods: 2);
        var stayed = await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync());
        var traded = await world.AddSpotAsync(
            world.Teams[0], await world.AddPlayerAsync("D"),
            start: world.Periods[0].StartDate, end: world.Periods[0].EndDate);

        await world.AddAssignmentAsync(stayed, world.Periods[0], active: true, points: 4, finalized: true);
        await world.AddAssignmentAsync(traded, world.Periods[0], active: true, points: 10, finalized: true);

        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        // A trade cannot move history: the ten points the departed player banked
        // in week 1 belong to this team permanently.
        Assert.Equal(14, row.Score);
        // But he is no longer on the roster, so he counts for neither the player
        // count nor the cap.
        Assert.Equal(1, row.PlayerCount);
    }

    [SqlFact]
    public async Task Standings_CapCountsOnlyTheCurrentRoster_AtTheLeaguesSeason()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        var held = await world.AddPlayerAsync("C", capHit: 9_000_000);
        var departed = await world.AddPlayerAsync("D", capHit: 5_000_000);
        var noContract = await world.AddPlayerAsync("G");
        // Same player, a different season's contract — must not be picked up.
        var wrongSeason = await world.AddPlayerAsync("C", capHit: 12_000_000, season: "20242025");

        await world.AddSpotAsync(world.Teams[0], held);
        await world.AddSpotAsync(world.Teams[0], departed,
            start: world.Periods[0].StartDate, end: world.Periods[0].EndDate);
        await world.AddSpotAsync(world.Teams[0], noContract);
        await world.AddSpotAsync(world.Teams[0], wrongSeason);

        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        // $9M held, plus the league's $1M default twice: once for the player
        // with no contract at all, once for the one whose only contract is for
        // another season. Neither is a gap to wait out — an unsigned player is
        // a permanent state, and charging him nothing understated every total.
        Assert.Equal(11_000_000, row.CapTotal);
        Assert.Equal(2, row.UnknownContracts);
        // Three open spots: the departed one is closed, but a player with no
        // contract on file still occupies a roster place.
        Assert.Equal(3, row.PlayerCount);
    }

    [SqlFact]
    public async Task Standings_ChargeForAnUnsignedPlayer_IsTheLeaguesOwnRule()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        world.League.DefaultCapHit = 2_500_000;
        await db.SaveChangesAsync();

        await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync("C", capHit: 9_000_000));
        await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync("G"));

        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        Assert.Equal(11_500_000, row.CapTotal);
        Assert.Equal(1, row.UnknownContracts);
    }

    [SqlFact]
    public async Task Standings_CarryUnsignedPlayersFree_WhenTheLeagueSetsTheDefaultToZero()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        // The behaviour every league had before 2026-08-05, still reachable —
        // as a rule now, not as an assumption baked into the view.
        world.League.DefaultCapHit = 0;
        await db.SaveChangesAsync();

        await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync("C", capHit: 9_000_000));
        await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync("G"));

        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        Assert.Equal(9_000_000, row.CapTotal);
        Assert.Equal(1, row.UnknownContracts);
    }

    // --- the two figures a forward-dated trade pulls apart ---
    //
    // Dates are taken relative to the real day rather than by pinning
    // SimulationState: that table is a single shared row, and writing it here
    // would move "today" underneath any standings query running in parallel.
    // Thirty days clears any midnight or timezone skew between the test host
    // and the view's Eastern date.
    private static DateOnly RealToday => DateOnly.FromDateTime(DateTime.Today);

    [SqlFact]
    public async Task Standings_StillCountAPlayerPromisedAwayInATradeThatHasNotLanded()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync("C", capHit: 9_000_000));
        // Traded, effective in a fortnight: his stint is already closed on the
        // books but he is still on this roster, and still scoring for it.
        await world.AddSpotAsync(
            world.Teams[0], await world.AddPlayerAsync("D", capHit: 4_000_000),
            end: RealToday.AddDays(30));

        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        // Today: both men, because benching the one leaving would hand his GM a
        // slot he cannot fill this week.
        Assert.Equal(2, row.PlayerCount);
        Assert.Equal(13_000_000, row.CapTotal);
        // Engaged: only the one who stays. This is the figure a new offer is
        // validated against, and counting the departing $4M there is what let a
        // GM commit the same cap space twice.
        Assert.Equal(1, row.EngagedPlayerCount);
        Assert.Equal(9_000_000, row.EngagedCapTotal);
    }

    [SqlFact]
    public async Task Standings_DoNotYetCountAPlayerAcquiredForNextWeek()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync("C", capHit: 9_000_000));
        // The other half of the same trade: a real spot, a real cap hit, and a
        // lineup row for next week — he simply is not here yet.
        await world.AddSpotAsync(
            world.Teams[0], await world.AddPlayerAsync("D", capHit: 6_000_000),
            start: RealToday.AddDays(30));

        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        // The cap gauge must not move on a signature. Nick, 2026-08-07: it
        // charges the spots that are active, and a spot starting next month is
        // not one of them.
        Assert.Equal(1, row.PlayerCount);
        Assert.Equal(9_000_000, row.CapTotal);
        // But the commitment is real the moment the trade is agreed, which is
        // the whole reason a trade is validated against this column.
        Assert.Equal(2, row.EngagedPlayerCount);
        Assert.Equal(15_000_000, row.EngagedCapTotal);
    }

    [SqlFact]
    public async Task Standings_EngagedAndTodayAgree_WhenNothingIsPending()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync("C", capHit: 9_000_000));
        await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync("G"));
        // Released in October: in neither figure, and his points stay put.
        await world.AddSpotAsync(
            world.Teams[0], await world.AddPlayerAsync("D", capHit: 5_000_000),
            start: world.Periods[0].StartDate, end: world.Periods[0].EndDate);

        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        // The ordinary case, which is every team until its first trade — the
        // two columns are the same aggregate one filter apart, and only the two
        // spots a trade creates can pull them apart.
        Assert.Equal(row.PlayerCount, row.EngagedPlayerCount);
        Assert.Equal(row.CapTotal, row.EngagedCapTotal);
        Assert.Equal(row.UnknownContracts, row.EngagedUnknownContracts);
        Assert.Equal(2, row.PlayerCount);
        Assert.Equal(10_000_000, row.CapTotal);
    }

    [SqlFact]
    public async Task Standings_ShowZerosForATeamThatHasNotPlayedYet()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        // A brand new team must appear in the standings at 0, not vanish from
        // them — which is what an inner join would have done.
        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        Assert.Equal(0, row.Score);
        Assert.Equal(0, row.PlayerCount);
        Assert.Equal(0, row.CapTotal);
    }

    /// <summary>Records one vote; a trade needs several, each from a distinct user.</summary>
    private static Entities.TradeVote Vote(int tradeId, int userId, int? favoredTeamId) => new()
    {
        TradeId = tradeId, UserId = userId, FavoredTeamId = favoredTeamId, VotedUtc = DateTime.UtcNow,
    };

    [SqlFact]
    public async Task PoolerTradeRecord_CountsWonLostFair_AndComputesTraderRating()
    {
        await using var db = SqlFixture.NewContext();
        // Team 0/1 are the trading parties; 2/3/4 exist only to cast votes,
        // same as fellow league members voting on someone else's trade.
        var world = await new TestWorld(db).CreateAsync(teams: 5);
        var (t0, t1, voterA, voterB, voterC) =
            (world.Teams[0], world.Teams[1], world.Teams[2].OwnerUserId, world.Teams[3].OwnerUserId, world.Teams[4].OwnerUserId);

        async Task<int> AddTrade(int proposerId, int counterpartyId)
        {
            var trade = new Entities.Trade
            {
                LeagueId = world.League.LeagueId,
                ProposerTeamId = proposerId,
                CounterpartyTeamId = counterpartyId,
                Status = Entities.TradeStatus.Processed,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Trades.Add(trade);
            await db.SaveChangesAsync();
            return trade.TradeId;
        }

        // Trade A: team0 proposes, wins 2-1.
        var a = await AddTrade(t0.TeamId, t1.TeamId);
        db.TradeVotes.AddRange(Vote(a, voterA, t0.TeamId), Vote(a, voterB, t0.TeamId), Vote(a, voterC, t1.TeamId));

        // Trade B: team0 is the *counterparty* this time, wins 2-0 with 1 fair —
        // exercises both sides of the UNION ALL in the same test.
        var b = await AddTrade(t1.TeamId, t0.TeamId);
        db.TradeVotes.AddRange(Vote(b, voterA, t0.TeamId), Vote(b, voterB, t0.TeamId), Vote(b, voterC, null));

        // Trade C: team0 proposes, loses 1-2.
        var c = await AddTrade(t0.TeamId, t1.TeamId);
        db.TradeVotes.AddRange(Vote(c, voterA, t1.TeamId), Vote(c, voterB, t1.TeamId), Vote(c, voterC, t0.TeamId));

        // Trade D: fair, 1-2 fair.
        var d = await AddTrade(t0.TeamId, t1.TeamId);
        db.TradeVotes.AddRange(Vote(d, voterA, null), Vote(d, voterB, null), Vote(d, voterC, t0.TeamId));

        await db.SaveChangesAsync();

        var row = await db.PoolerTradeRecords.SingleAsync(r => r.TeamId == t0.TeamId);

        Assert.Equal(4, row.TradesTotal);
        Assert.Equal(2, row.TradesWon);
        Assert.Equal(1, row.TradesLost);
        Assert.Equal(1, row.TradesFair);
        // 50 + 50 * (2 - 1) / 3
        Assert.NotNull(row.TraderRating);
        Assert.Equal(66.67, row.TraderRating!.Value, precision: 2);
    }

    [SqlFact]
    public async Task PoolerTradeRecord_RatingIsNull_WhenNoTradeHasEverBeenDecided()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 4);
        var (t0, t1) = (world.Teams[0], world.Teams[1]);

        var trade = new Entities.Trade
        {
            LeagueId = world.League.LeagueId,
            ProposerTeamId = t0.TeamId,
            CounterpartyTeamId = t1.TeamId,
            Status = Entities.TradeStatus.Processed,
            CreatedUtc = DateTime.UtcNow,
        };
        db.Trades.Add(trade);
        await db.SaveChangesAsync();
        db.TradeVotes.AddRange(
            Vote(trade.TradeId, world.Teams[2].OwnerUserId, null),
            Vote(trade.TradeId, world.Teams[3].OwnerUserId, null));
        await db.SaveChangesAsync();

        var row = await db.PoolerTradeRecords.SingleAsync(r => r.TeamId == t0.TeamId);

        Assert.Equal(1, row.TradesFair);
        Assert.Equal(0, row.TradesWon);
        Assert.Equal(0, row.TradesLost);
        // A GM who only ever makes fair trades sits at "not enough data", not
        // at a misleadingly neutral 50.
        Assert.Null(row.TraderRating);
    }

    [SqlFact]
    public async Task PoolerTradeRecord_HasNoRow_ForATeamWithNoProcessedTrades()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        var row = await db.PoolerTradeRecords.SingleOrDefaultAsync(r => r.TeamId == world.Teams[0].TeamId);

        // Unlike vStandings (which zero-fills every team via a LEFT JOIN from
        // Teams), a trade record with nothing to report has no row at all.
        Assert.Null(row);
    }

    [SqlFact]
    public async Task PoolerTradeRecord_AnExactTie_CountsTowardNeitherWonNorLostNorFair()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 4);
        var (t0, t1) = (world.Teams[0], world.Teams[1]);

        var trade = new Entities.Trade
        {
            LeagueId = world.League.LeagueId,
            ProposerTeamId = t0.TeamId,
            CounterpartyTeamId = t1.TeamId,
            Status = Entities.TradeStatus.Processed,
            CreatedUtc = DateTime.UtcNow,
        };
        db.Trades.Add(trade);
        await db.SaveChangesAsync();
        // 1 vote each way, none fair — a genuine split decision.
        db.TradeVotes.AddRange(
            Vote(trade.TradeId, world.Teams[2].OwnerUserId, t0.TeamId),
            Vote(trade.TradeId, world.Teams[3].OwnerUserId, t1.TeamId));
        await db.SaveChangesAsync();

        var row = await db.PoolerTradeRecords.SingleAsync(r => r.TeamId == t0.TeamId);

        Assert.Equal(1, row.TradesTotal);
        Assert.Equal(0, row.TradesWon);
        Assert.Equal(0, row.TradesLost);
        Assert.Equal(0, row.TradesFair);
        Assert.Null(row.TraderRating);
    }

    [SqlFact]
    public async Task CockcoinBalance_SumsEveryAward()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var userId = world.Teams[0].OwnerUserId;

        db.CockcoinAwards.AddRange(
            new Entities.CockcoinAward { UserId = userId, Amount = 2, Reason = "trade-vote", AwardedUtc = DateTime.UtcNow },
            new Entities.CockcoinAward { UserId = userId, Amount = 2, Reason = "trade-vote", AwardedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var row = await db.CockcoinBalances.SingleAsync(b => b.UserId == userId);

        Assert.Equal(4, row.Balance);
    }

    [SqlFact]
    public async Task CockcoinBalance_HasNoRow_ForAUserWhoHasNeverEarnedAny()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        // Everyone starts at 0, but that's the API turning "no row" into 0 —
        // the view itself just has nothing to report yet.
        var row = await db.CockcoinBalances.SingleOrDefaultAsync(b => b.UserId == world.Teams[0].OwnerUserId);

        Assert.Null(row);
    }

    // --- the Équipe slot ---

    [SqlFact]
    public async Task Standings_DoNotChargeTheFranchiseAgainstTheCap()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync("C", capHit: 9_000_000));
        await world.AddFranchiseSpotAsync(world.Teams[0], "MTL");

        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        // **The regression the whole view change exists to prevent.** A
        // franchise has no contract, so without the exclusion it would be
        // billed the league's $1M default, counted as an unsigned player, and
        // pushed the roster one over its own maximum — every one of the three
        // wrong, and only the first visible.
        Assert.Equal(9_000_000, row.CapTotal);
        Assert.Equal(0, row.UnknownContracts);
        Assert.Equal(1, row.PlayerCount);
    }

    [SqlFact]
    public async Task Standings_CountTheFranchisesPoints()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var period = world.Periods[0];

        var player = await world.AddPlayerAsync("C");
        var playerSpot = await world.AddSpotAsync(world.Teams[0], player);
        await world.AddAssignmentAsync(playerSpot, period, active: true, points: 4);

        var franchiseSpot = await world.AddFranchiseSpotAsync(world.Teams[0], "MTL");
        // Les Mordus: 2 per win, 1 per overtime loss. Two wins and an OT loss.
        await world.AddFranchiseAssignmentAsync(
            franchiseSpot, period, points: 5, teamWins: 2, teamLosses: 1, teamOtLosses: 1);

        var row = await db.Standings.SingleAsync(s => s.TeamId == world.Teams[0].TeamId);

        // Excluded from the cap, included in the score: those are different
        // questions, and the franchise answers them differently.
        Assert.Equal(9, row.Score);
        // The franchise reports a record, not a workload — so the denominator
        // behind points-per-game stays about players.
        Assert.Equal(1, row.RosterGamesPlayed);
    }

    [SqlFact]
    public async Task RosterSpotTotals_CarryTheFranchisesSeasonRecord()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(periods: 2);

        var spot = await world.AddFranchiseSpotAsync(world.Teams[0], "COL");
        await world.AddFranchiseAssignmentAsync(
            spot, world.Periods[0], points: 5, teamWins: 2, teamLosses: 1, teamOtLosses: 1);
        await world.AddFranchiseAssignmentAsync(
            spot, world.Periods[1], points: 6, teamWins: 3, teamLosses: 0, teamOtLosses: 0);

        var row = await db.RosterSpotTotals.SingleAsync(v => v.RosterSpotId == spot.RosterSpotId);

        Assert.Equal("COL", row.FranchiseAbbrev);
        Assert.Null(row.PlayerId);
        Assert.Equal(5, row.ActiveTeamWins);
        Assert.Equal(1, row.ActiveTeamLosses);
        Assert.Equal(1, row.ActiveTeamOtLosses);
        Assert.Equal(11, row.ActivePoints);
        // Never benched, so this half of the view is always empty for a
        // franchise — the Team grid shows a dash rather than a zero.
        Assert.Equal(0, row.BenchPoints);
    }
}
