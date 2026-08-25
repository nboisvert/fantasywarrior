using FantasyWarrior.Core.Drafts;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// What the database refuses about a draft selection.
///
/// The draft has no pick clock, so two GMs really can submit at the same
/// instant, and the steal segment has no entitlement row to claim. These
/// constraints are not belt-and-braces over an application check - for the
/// steal segment they are the <i>only</i> thing standing between an
/// asynchronous draft and a double pick.
/// </summary>
[Collection(SqlCollection.Name)]
public class DraftSelectionConstraintTests
{
    private static async Task<LeagueSeason> SeasonAsync(
        FantasyWarriorDbContext db, TestWorld world, int number = 2)
    {
        var season = new LeagueSeason
        {
            LeagueId = world.League.LeagueId,
            Season = world.League.Season,
            Number = number,
            Phase = LeagueSeasonPhase.Drafting,
            StartedUtc = DateTime.UtcNow,
        };
        db.LeagueSeasons.Add(season);
        await db.SaveChangesAsync();
        return season;
    }

    private static DraftSelection Steal(
        LeagueSeason season, int overallIndex, int teamId, long? playerId, int? victimTeamId) =>
        new()
        {
            LeagueSeasonId = season.LeagueSeasonId,
            OverallIndex = overallIndex,
            Segment = DraftSegment.Steal,
            Round = 1,
            TeamId = teamId,
            PlayerId = playerId,
            StolenFromTeamId = victimTeamId,
            MadeUtc = DateTime.UtcNow,
        };

    [SqlFact]
    public async Task TwoGmsCannotTakeTheSameTurn()
    {
        // THE race the table exists for. Both GMs read "8 selections made", both
        // believe they are on turn 8, and they pick different players - so no
        // other constraint in the schema would notice.
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 3);
        var season = await SeasonAsync(db, world);

        var first = await world.AddPlayerAsync();
        var second = await world.AddPlayerAsync();

        db.DraftSelections.Add(Steal(season, 8, world.Teams[0].TeamId, first.PlayerId, world.Teams[2].TeamId));
        await db.SaveChangesAsync();

        db.DraftSelections.Add(Steal(season, 8, world.Teams[1].TeamId, second.PlayerId, world.Teams[2].TeamId));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlFact]
    public async Task TheSamePlayerCannotBeDraftedTwiceInOneDraft()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 3);
        var season = await SeasonAsync(db, world);
        var player = await world.AddPlayerAsync();

        db.DraftSelections.Add(Steal(season, 0, world.Teams[0].TeamId, player.PlayerId, world.Teams[2].TeamId));
        await db.SaveChangesAsync();

        db.DraftSelections.Add(Steal(season, 1, world.Teams[1].TeamId, player.PlayerId, world.Teams[2].TeamId));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlFact]
    public async Task PassedTurnsDoNotCollideWithEachOther()
    {
        // A pass has no player, and the "one per player" index is filtered so
        // that several nulls are fine. Without the filter the second pass in a
        // draft would be rejected - and a GM facing an empty pool has no other
        // move.
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 3);
        var season = await SeasonAsync(db, world);

        db.DraftSelections.Add(Steal(season, 0, world.Teams[0].TeamId, playerId: null, victimTeamId: null));
        db.DraftSelections.Add(Steal(season, 1, world.Teams[1].TeamId, playerId: null, victimTeamId: null));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.DraftSelections.CountAsync(s => s.LeagueSeasonId == season.LeagueSeasonId));
    }

    [SqlFact]
    public async Task APassCannotRobAnybody()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 2);
        var season = await SeasonAsync(db, world);

        db.DraftSelections.Add(
            Steal(season, 0, world.Teams[0].TeamId, playerId: null, victimTeamId: world.Teams[1].TeamId));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlFact]
    public async Task AStealCannotSpendAnEntitlement()
    {
        // Steal turns are not tradable and deliberately have no row to point
        // at. A steal carrying a DraftPickId would be a draft nobody could read
        // back.
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 2);
        var season = await SeasonAsync(db, world);
        var player = await world.AddPlayerAsync();
        var pick = await AddPickAsync(db, world, round: 1);

        var selection = Steal(season, 0, world.Teams[0].TeamId, player.PlayerId, world.Teams[1].TeamId);
        selection.DraftPickId = pick.DraftPickId;
        db.DraftSelections.Add(selection);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlFact]
    public async Task ARookieSelectionMustSpendAnEntitlementAndRobNobody()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 2);
        var season = await SeasonAsync(db, world);
        var player = await world.AddPlayerAsync();

        var noPick = Steal(season, 0, world.Teams[0].TeamId, player.PlayerId, victimTeamId: null);
        noPick.Segment = DraftSegment.Rookie;
        db.DraftSelections.Add(noPick);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlFact]
    public async Task OneEntitlementCannotBeSpentTwice()
    {
        // This is what makes DraftPick.UsedUtc and DraftSelections unable to
        // disagree, and it replaces a conditional update a future caller could
        // forget to write.
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 2);
        var season = await SeasonAsync(db, world);
        var pick = await AddPickAsync(db, world, round: 1);

        var first = await world.AddPlayerAsync();
        var second = await world.AddPlayerAsync();

        db.DraftSelections.Add(Rookie(season, 0, world.Teams[0].TeamId, first.PlayerId, pick.DraftPickId));
        await db.SaveChangesAsync();

        db.DraftSelections.Add(Rookie(season, 1, world.Teams[1].TeamId, second.PlayerId, pick.DraftPickId));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlFact]
    public async Task TwoSeasonsOfTheSameLeagueDraftIndependently()
    {
        // The turn index is scoped to the league season, not the league: last
        // summer's picks must not count in this summer's turn arithmetic.
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 2);

        var older = new LeagueSeason
        {
            LeagueId = world.League.LeagueId,
            // A league season is unique on its NHL season too, which is the
            // point: a league plays each one once.
            Season = Season.Previous(world.League.Season),
            Number = 1,
            Phase = LeagueSeasonPhase.Complete,
            StartedUtc = DateTime.UtcNow,
        };
        db.LeagueSeasons.Add(older);
        await db.SaveChangesAsync();

        var current = await SeasonAsync(db, world);
        var player = await world.AddPlayerAsync();
        var other = await world.AddPlayerAsync();

        db.DraftSelections.Add(Steal(older, 0, world.Teams[0].TeamId, player.PlayerId, world.Teams[1].TeamId));
        db.DraftSelections.Add(Steal(current, 0, world.Teams[0].TeamId, other.PlayerId, world.Teams[1].TeamId));

        await db.SaveChangesAsync();

        Assert.Equal(1, await db.DraftSelections.CountAsync(s => s.LeagueSeasonId == current.LeagueSeasonId));
    }

    private static DraftSelection Rookie(
        LeagueSeason season, int overallIndex, int teamId, long playerId, int draftPickId) =>
        new()
        {
            LeagueSeasonId = season.LeagueSeasonId,
            OverallIndex = overallIndex,
            Segment = DraftSegment.Rookie,
            Round = 1,
            TeamId = teamId,
            PlayerId = playerId,
            DraftPickId = draftPickId,
            MadeUtc = DateTime.UtcNow,
        };

    private static async Task<DraftPick> AddPickAsync(
        FantasyWarriorDbContext db, TestWorld world, int round)
    {
        var pick = new DraftPick
        {
            LeagueId = world.League.LeagueId,
            Year = Season.StartYear(world.League.Season),
            Round = round,
            OriginalTeamId = world.Teams[0].TeamId,
            CurrentTeamId = world.Teams[0].TeamId,
            CreatedUtc = DateTime.UtcNow,
        };
        db.DraftPicks.Add(pick);
        await db.SaveChangesAsync();
        return pick;
    }
}
