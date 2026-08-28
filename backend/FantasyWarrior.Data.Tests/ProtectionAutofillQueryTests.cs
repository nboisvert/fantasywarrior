using FantasyWarrior.Core.Drafts;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Rosters;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// The database half of the protection autofill — the part
/// <c>ProtectionAutofillTests</c> cannot reach.
///
/// Two things here are database-shaped and nothing else proves them: that the
/// candidate projection sees the same spots the steal pool sees (same
/// <see cref="RosterWindow.Committed"/> filter, franchises dropped), and that
/// the clear-then-set write translates and lands. The endpoint rewrites a whole
/// league on every press, so a write that silently matched nothing would look
/// exactly like "nobody qualified".
/// </summary>
[Collection(SqlCollection.Name)]
public class ProtectionAutofillQueryTests
{
    /// <summary>The endpoint's projection, verbatim.</summary>
    private static Task<List<ProtectionCandidate>> CandidatesAsync(
        FantasyWarriorDbContext db, int leagueId) =>
        db.RosterSpots
            .AsNoTracking()
            .Where(s => s.LeagueId == leagueId && s.PlayerId != null)
            .Where(RosterWindow.Committed())
            .Select(s => new ProtectionCandidate(
                s.RosterSpotId, s.TeamId, s.PlayerId!.Value,
                s.Player!.PositionGroup, s.Player.CareerNhlGames, 0d))
            .ToListAsync();

    private static async Task<Player> VeteranAsync(TestWorld world, FantasyWarriorDbContext db)
    {
        var player = await world.AddPlayerAsync();
        player.CareerNhlGames = 400;
        await db.SaveChangesAsync();
        return player;
    }

    /// <summary>
    /// A franchise spot carries no PlayerId. It once emptied a whole list by
    /// riding along as a null, which is why the filter is asserted rather than
    /// assumed.
    /// </summary>
    [SqlFact]
    public async Task Candidates_SkipFranchiseSpots_AndClosedOnes()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var team = world.Teams[0];

        var held = await VeteranAsync(world, db);
        await world.AddSpotAsync(team, held);

        var released = await VeteranAsync(world, db);
        await world.AddSpotAsync(team, released, end: world.Periods[0].EndDate);

        await world.AddFranchiseAsync("BOS");
        await world.AddFranchiseSpotAsync(team, "BOS");

        var candidates = await CandidatesAsync(db, world.League.LeagueId);

        Assert.Equal([held.PlayerId], candidates.Select(c => c.PlayerId));
    }

    /// <summary>
    /// The whole round trip: project, choose, clear, write. The clear runs
    /// unconditionally so that lowering the slot count releases yesterday's
    /// choices instead of stacking on top of them.
    /// </summary>
    [SqlFact]
    public async Task ClearThenSet_LeavesExactlyTheChosenSpotsProtected()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var team = world.Teams[0];

        var spots = new List<RosterSpot>();
        foreach (var _ in Enumerable.Range(0, 3))
            spots.Add(await world.AddSpotAsync(team, await VeteranAsync(world, db)));

        // A stale protection from an earlier run, on a man nobody would pick now.
        spots[2].ProtectionStatus = RosterProtectionStatus.Protected;
        await db.SaveChangesAsync();

        var candidates = await CandidatesAsync(db, world.League.LeagueId);
        // Points are all zero here, so the tie-break decides — which is the
        // property that makes this reproducible at all.
        var chosen = ProtectionAutofill.Choose(candidates, slots: 2);
        Assert.Equal(2, chosen.Count);

        await db.RosterSpots
            .Where(s => s.LeagueId == world.League.LeagueId
                        && s.ProtectionStatus != RosterProtectionStatus.Unprotected)
            .ExecuteUpdateAsync(u =>
                u.SetProperty(s => s.ProtectionStatus, RosterProtectionStatus.Unprotected));

        var written = await db.RosterSpots
            .Where(s => chosen.Contains(s.RosterSpotId))
            .ExecuteUpdateAsync(u =>
                u.SetProperty(s => s.ProtectionStatus, RosterProtectionStatus.Protected));

        Assert.Equal(2, written);

        var nowProtected = await db.RosterSpots
            .AsNoTracking()
            .Where(s => s.LeagueId == world.League.LeagueId
                        && s.ProtectionStatus == RosterProtectionStatus.Protected)
            .Select(s => s.RosterSpotId)
            .ToListAsync();

        Assert.Equal(chosen.Order(), nowProtected.Order());
    }
}
