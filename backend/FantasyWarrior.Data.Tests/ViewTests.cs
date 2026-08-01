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

        Assert.Equal(9_000_000, row.CapTotal);
        // Three open spots: the departed one is closed, but a player with no
        // contract on file still occupies a roster place.
        Assert.Equal(3, row.PlayerCount);
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
}
