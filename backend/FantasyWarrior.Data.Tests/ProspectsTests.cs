using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Players;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// Who the Team grid sinks to the bottom, and — more to the point — who it
/// must not.
///
/// The rule is one line to state ("no NHL game ever played") and has three
/// ways to go wrong, all of them silent: reading the wrong column, counting a
/// junior season as a career, and mistaking "never checked" for "never
/// played". One test each.
/// </summary>
[Collection(SqlCollection.Name)]
public class ProspectsTests
{
    private static PlayerCareerSeasonStat Season(
        long playerId, string league, string season, int gamesPlayed) =>
        new()
        {
            PlayerId = playerId,
            Season = season,
            GameType = GameType.RegularSeason,
            LeagueAbbrev = league,
            GamesPlayed = gamesPlayed,
        };

    [SqlFact]
    public async Task APlayerWithNoNhlGameIsAProspect_HoweverLongHisJuniorCareer()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var player = await world.AddPlayerAsync();
        player.CareerStatsSyncedUtc = DateTime.UtcNow;
        // Four full junior seasons. None of them is an NHL game.
        db.PlayerCareerSeasonStats.AddRange(
            Season(player.PlayerId, "WHL", "20222023", 68),
            Season(player.PlayerId, "WHL", "20232024", 64),
            Season(player.PlayerId, "AHL", "20242025", 51));
        await db.SaveChangesAsync();

        var prospects = await Prospects.ForAsync(db, [player.PlayerId]);

        Assert.Contains(player.PlayerId, prospects);
    }

    [SqlFact]
    public async Task OneNhlGameEndsIt()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var player = await world.AddPlayerAsync();
        player.CareerStatsSyncedUtc = DateTime.UtcNow;
        db.PlayerCareerSeasonStats.AddRange(
            Season(player.PlayerId, "OHL", "20242025", 62),
            Season(player.PlayerId, "NHL", "20252026", 1));
        await db.SaveChangesAsync();

        var prospects = await Prospects.ForAsync(db, [player.PlayerId]);

        Assert.DoesNotContain(player.PlayerId, prospects);
    }

    /// <summary>
    /// The NHL's payload carries a season line for a player called up and never
    /// dressed. Being on the sheet is not having played, so the row alone must
    /// not disqualify him.
    /// </summary>
    [SqlFact]
    public async Task AnNhlRowOfZeroGamesIsStillNoGames()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var player = await world.AddPlayerAsync();
        player.CareerStatsSyncedUtc = DateTime.UtcNow;
        db.PlayerCareerSeasonStats.Add(Season(player.PlayerId, "NHL", "20252026", 0));
        await db.SaveChangesAsync();

        var prospects = await Prospects.ForAsync(db, [player.PlayerId]);

        Assert.Contains(player.PlayerId, prospects);
    }

    /// <summary>
    /// The dangerous case. A player career-sync has never reached has no rows
    /// at all, which looks exactly like a career of no NHL games — and calling
    /// him a prospect would sink a veteran to the bottom of his own GM's grid.
    /// </summary>
    [SqlFact]
    public async Task NeverCheckedIsNotTheSameAsNeverPlayed()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var unchecked_ = await world.AddPlayerAsync();
        var checkedAndEmpty = await world.AddPlayerAsync();
        checkedAndEmpty.CareerStatsSyncedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var prospects = await Prospects.ForAsync(db, [unchecked_.PlayerId, checkedAndEmpty.PlayerId]);

        Assert.DoesNotContain(unchecked_.PlayerId, prospects);
        Assert.Contains(checkedAndEmpty.PlayerId, prospects);
    }

    /// <summary>
    /// <see cref="Player.Status"/> already says "prospect" and means something
    /// else — not on an NHL club's season roster. A player can carry
    /// <see cref="PlayerStatus.Nhl"/> and still never have played; that is the
    /// larger half of the real Mordus rosters, so reading Status instead would
    /// have missed most of them.
    /// </summary>
    [SqlFact]
    public async Task StatusIsNotTheRule()
    {
        await using var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        var dressedNeverPlayed = await world.AddPlayerAsync();
        dressedNeverPlayed.Status = PlayerStatus.Nhl;
        dressedNeverPlayed.CareerStatsSyncedUtc = DateTime.UtcNow;

        var labelledProspectButPlayed = await world.AddPlayerAsync();
        labelledProspectButPlayed.Status = PlayerStatus.Prospect;
        labelledProspectButPlayed.CareerStatsSyncedUtc = DateTime.UtcNow;
        db.PlayerCareerSeasonStats.Add(
            Season(labelledProspectButPlayed.PlayerId, "NHL", "20242025", 12));
        await db.SaveChangesAsync();

        var prospects = await Prospects.ForAsync(
            db, [dressedNeverPlayed.PlayerId, labelledProspectButPlayed.PlayerId]);

        Assert.Contains(dressedNeverPlayed.PlayerId, prospects);
        Assert.DoesNotContain(labelledProspectButPlayed.PlayerId, prospects);
    }
}
