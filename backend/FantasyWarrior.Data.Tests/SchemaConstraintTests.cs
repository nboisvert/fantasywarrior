using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// The rules the database enforces on its own.
///
/// These matter more than they look: each one replaces an application-level
/// check that could be bypassed, forgotten, or lost to a race. "One owner per
/// player per league" in particular was a scan of every team's roster array
/// under Firestore — correct in the common case, and unable to stop two
/// simultaneous adds from both succeeding.
/// </summary>
[Collection(SqlCollection.Name)]
public class SchemaConstraintTests
{
    [SqlFact]
    public async Task PositionGroup_IsDerivedByTheDatabase_NotWrittenByTheApp()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();

        var centre = await world.AddPlayerAsync("C");
        var winger = await world.AddPlayerAsync("L");
        var right = await world.AddPlayerAsync("R");
        var defence = await world.AddPlayerAsync("D");
        var goalie = await world.AddPlayerAsync("G");

        // Reload: the value is computed on insert, so it only appears after a
        // round-trip. That is the point — no code path can write it wrong.
        db.ChangeTracker.Clear();
        var byId = await db.Players
            .Where(p => new[] { centre.PlayerId, winger.PlayerId, right.PlayerId, defence.PlayerId, goalie.PlayerId }
                .Contains(p.PlayerId))
            .ToDictionaryAsync(p => p.PlayerId, p => p.PositionGroup);

        Assert.Equal("F", byId[centre.PlayerId]);
        Assert.Equal("F", byId[winger.PlayerId]);
        Assert.Equal("F", byId[right.PlayerId]);
        Assert.Equal("D", byId[defence.PlayerId]);
        Assert.Equal("G", byId[goalie.PlayerId]);
    }

    [SqlFact]
    public async Task APlayerCannotBeOnTwoRostersInTheSameLeagueAtOnce()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 2);
        var player = await world.AddPlayerAsync();

        await world.AddSpotAsync(world.Teams[0], player);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => world.AddSpotAsync(world.Teams[1], player));
        Assert.Contains("UX_RosterSpots_OneOpenSpotPerPlayerPerLeague", ex.InnerException?.Message ?? "");
    }

    [SqlFact]
    public async Task AClosedSpotDoesNotBlockANewOne()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 2);
        var player = await world.AddPlayerAsync();

        // Traded away on the Sunday, picked up by the other team on the Monday.
        await world.AddSpotAsync(world.Teams[0], player,
            start: new DateOnly(2025, 10, 6), end: new DateOnly(2025, 10, 12));
        var second = await world.AddSpotAsync(world.Teams[1], player, start: new DateOnly(2025, 10, 13));

        Assert.True(second.RosterSpotId > 0);
    }

    [SqlFact]
    public async Task TheSamePlayerCanBeOwnedInTwoDifferentLeagues()
    {
        await using var db = SqlFixture.NewContext();
        var a = await new TestWorld(db).CreateAsync();
        var b = await new TestWorld(db).CreateAsync();

        var player = await a.AddPlayerAsync();

        await a.AddSpotAsync(a.Teams[0], player);
        var inB = await b.AddSpotAsync(b.Teams[0], player);

        // Multi-tenancy: uniqueness is scoped to a league, never global.
        Assert.True(inB.RosterSpotId > 0);
    }

    [SqlFact]
    public async Task ARosterSpotGetsAtMostOneAssignmentPerWeek()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var spot = await world.AddSpotAsync(world.Teams[0], await world.AddPlayerAsync());

        await world.AddAssignmentAsync(spot, world.Periods[0], active: true, points: 5);

        // This is what makes the nightly job safe to re-run: it can only ever
        // update the week's row, never add a second one alongside it.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => world.AddAssignmentAsync(spot, world.Periods[0], active: true, points: 5));
    }

    [SqlFact]
    public async Task ATradeAssetIsAPlayerOrAPick_NeverBothAndNeverNeither()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync(teams: 2);
        var player = await world.AddPlayerAsync();

        var trade = new Trade
        {
            LeagueId = world.League.LeagueId,
            ProposerTeamId = world.Teams[0].TeamId,
            CounterpartyTeamId = world.Teams[1].TeamId,
            Status = TradeStatus.Pending,
            CreatedUtc = DateTime.UtcNow,
        };
        db.Trades.Add(trade);
        await db.SaveChangesAsync();

        async Task<Exception?> TryAdd(TradeAssetType type, long? playerId, int? pickId)
        {
            db.TradeAssets.Add(new TradeAsset
            {
                TradeId = trade.TradeId,
                FromTeamId = world.Teams[0].TeamId,
                ToTeamId = world.Teams[1].TeamId,
                AssetType = type,
                PlayerId = playerId,
                DraftPickId = pickId,
            });
            try
            {
                await db.SaveChangesAsync();
                return null;
            }
            catch (DbUpdateException ex)
            {
                db.ChangeTracker.Clear();
                return ex;
            }
        }

        // A "player" asset carrying no player would silently mean an empty thing
        // changed hands.
        Assert.NotNull(await TryAdd(TradeAssetType.Player, null, null));
        // A type that disagrees with the column that is set is the same bug,
        // one step later.
        Assert.NotNull(await TryAdd(TradeAssetType.DraftPick, player.PlayerId, null));

        Assert.Null(await TryAdd(TradeAssetType.Player, player.PlayerId, null));
    }

    [SqlFact]
    public async Task ThereCanOnlyEverBeOneSimulationCursor()
    {
        await using var db = SqlFixture.NewContext();

        db.SimulationState.Add(new SimulationState
        {
            SimulationStateId = 2,
            AsOfDate = new DateOnly(2025, 10, 20),
            Season = "20252026",
            Enabled = true,
            UpdatedUtc = DateTime.UtcNow,
        });

        // Two cursors would mean two answers to "what day is it", which is the
        // one question the whole app has to agree on.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlFact]
    public async Task TheNhlFranchisesAreSeededByTheMigration()
    {
        await using var db = SqlFixture.NewContext();

        Assert.Equal(32, await db.NhlTeams.CountAsync());
        Assert.Equal("Montreal Canadiens", (await db.NhlTeams.FindAsync("MTL"))!.Name);
        // Utah is the one that changes; it is "Mammoth" from 2025-26.
        Assert.Equal("Utah Mammoth", (await db.NhlTeams.FindAsync("UTA"))!.Name);
    }
}
