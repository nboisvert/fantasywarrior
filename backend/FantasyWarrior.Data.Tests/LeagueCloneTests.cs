using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Leagues;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// The clone is entirely database-shaped — it is a copy, and what it does not
/// copy is the interesting half. Nothing but the real schema proves that the
/// weeks stayed behind, that a closed spot did not ride along, and that a
/// forward-dated spot lands somewhere a "held today" query can still see it.
/// </summary>
[Collection(SqlCollection.Name)]
public class LeagueCloneTests
{
    private static readonly DateOnly Today = new(2025, 12, 23);

    [SqlFact]
    public async Task Copies_RulesTeamsAndOpenSpots()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        db.LeagueScoringRules.Add(new LeagueScoringRule
        {
            LeagueId = world.League.LeagueId, StatKey = "goals", PointValue = 3,
        });
        world.League.ProtectionSlots = 9;
        world.League.StealRounds = 2;
        world.League.MaxLossesPerTeam = 2;
        await db.SaveChangesAsync();

        var held = await world.AddPlayerAsync();
        await world.AddSpotAsync(world.Teams[0], held);
        await world.AddFranchiseSpotAsync(world.Teams[0], "BOS");
        await world.AddSpotAsync(world.Teams[1], await world.AddPlayerAsync());

        var clone = await LeagueClone.CreateAsync(db, world.League, $"Copy {world.League.JoinCode}", Today);

        Assert.Equal(2, clone.PlayerSpots);
        Assert.Equal(1, clone.FranchiseSpots);
        Assert.NotEqual(world.League.JoinCode, clone.League.JoinCode);

        // The rules a draft reads, carried over — without them the copy would
        // price protections and steals differently from the league it came from.
        Assert.Equal(9, clone.League.ProtectionSlots);
        Assert.Equal(2, clone.League.StealRounds);
        Assert.Equal(2, clone.League.MaxLossesPerTeam);
        Assert.Equal(world.League.CapAmount, clone.League.CapAmount);

        var scale = await db.LeagueScoringRules
            .Where(r => r.LeagueId == clone.League.LeagueId)
            .ToDictionaryAsync(r => r.StatKey, r => r.PointValue);
        Assert.Equal(3, scale["goals"]);

        var teams = await db.Teams.Where(t => t.LeagueId == clone.League.LeagueId).ToListAsync();
        Assert.Equal(2, teams.Count);
        Assert.Equal(
            world.Teams.Select(t => t.OwnerUserId).Order(),
            teams.Select(t => t.OwnerUserId).Order());

        var members = await db.LeagueMembers.CountAsync(m => m.LeagueId == clone.League.LeagueId);
        Assert.Equal(2, members);
    }

    /// <summary>
    /// The point of the whole thing: the copy has never played. A borrowed week
    /// would put points in a standings table nobody earned.
    /// </summary>
    [SqlFact]
    public async Task LeavesTheWeeksBehind()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        var spot = await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync());
        await world.AddAssignmentAsync(spot, world.Periods[0], active: true, points: 12);
        db.TeamPeriodLineups.Add(new TeamPeriodLineup
        {
            TeamId = world.Teams[0].TeamId,
            PeriodId = world.Periods[0].PeriodId,
            SetBy = "gm",
            SubmittedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var clone = await LeagueClone.CreateAsync(db, world.League, $"Copy {world.League.JoinCode}", Today);

        var cloneTeamIds = await db.Teams
            .Where(t => t.LeagueId == clone.League.LeagueId)
            .Select(t => t.TeamId)
            .ToListAsync();

        var assignments = await db.RosterAssignments
            .CountAsync(a => a.RosterSpot!.LeagueId == clone.League.LeagueId);
        Assert.Equal(0, assignments);

        var lineups = await db.TeamPeriodLineups.CountAsync(l => cloneTeamIds.Contains(l.TeamId));
        Assert.Equal(0, lineups);

        var seasons = await db.LeagueSeasons.CountAsync(s => s.LeagueId == clone.League.LeagueId);
        Assert.Equal(0, seasons);

        // The source keeps everything it had.
        Assert.Equal(1, await db.RosterAssignments.CountAsync(a => a.RosterSpotId == spot.RosterSpotId));
    }

    /// <summary>
    /// A released player is history, not a roster. A player arriving on a Monday
    /// still to come is a roster — but the trade that would land him was not
    /// copied, so his spot has to open on a date "held today" can already see,
    /// or he would be invisible to every screen and to the salary cap forever.
    /// </summary>
    [SqlFact]
    public async Task DropsClosedSpots_AndClampsFutureStartsToToday()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        await world.AddSpotAsync(
            world.Teams[0], await world.AddPlayerAsync(), end: world.Periods[0].EndDate);

        var arriving = await world.AddPlayerAsync();
        await world.AddSpotAsync(world.Teams[0], arriving, start: Today.AddDays(5));

        var settled = await world.AddPlayerAsync();
        await world.AddSpotAsync(world.Teams[0], settled, start: new DateOnly(2025, 10, 6));

        var clone = await LeagueClone.CreateAsync(db, world.League, $"Copy {world.League.JoinCode}", Today);

        var spots = await db.RosterSpots
            .Where(s => s.LeagueId == clone.League.LeagueId)
            .ToListAsync();

        Assert.Equal(2, spots.Count);
        Assert.Equal(Today, spots.Single(s => s.PlayerId == arriving.PlayerId).StartDate);
        Assert.Equal(new DateOnly(2025, 10, 6), spots.Single(s => s.PlayerId == settled.PlayerId).StartDate);

        // Nothing points back at a trade or a pick that was never copied.
        Assert.All(spots, s =>
        {
            Assert.Equal(RosterSpotStartReason.Draft, s.StartReason);
            Assert.Null(s.StartTradeId);
            Assert.Null(s.StartDraftPickId);
            Assert.Equal(RosterProtectionStatus.Unprotected, s.ProtectionStatus);
        });
    }

    /// <summary>
    /// Membership is what puts a league in someone's list, so it is also the
    /// switch between a private rehearsal and fourteen people finding a new pool
    /// on their dashboard.
    /// </summary>
    [SqlFact]
    public async Task CommissionerOnly_LeavesTheOtherGmsOut()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 3);

        var clone = await LeagueClone.CreateAsync(
            db, world.League, $"Copy {world.League.JoinCode}", Today, everyOwnerJoins: false);

        var members = await db.LeagueMembers
            .Where(m => m.LeagueId == clone.League.LeagueId)
            .Select(m => m.UserId)
            .ToListAsync();

        Assert.Equal([world.League.CommissionerUserId], members);

        // Every GM still has a team — only the door is closed.
        Assert.Equal(3, await db.Teams.CountAsync(t => t.LeagueId == clone.League.LeagueId));
    }
}
