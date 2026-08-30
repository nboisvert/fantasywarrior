using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Seasons;
using CoreSeason = FantasyWarrior.Core.Seasons.Season;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// The declared calendar, read back.
///
/// Every test owns its own decade of season strings: the table is keyed by the
/// season itself and shared across the run, so two tests declaring "20262027"
/// would collide on the primary key. The migration seeds 20252026 (the season
/// already played), which is why no test picks a date inside it.
/// </summary>
[Collection(SqlCollection.Name)]
public class SeasonLookupTests
{
    private static async Task<FantasyWarriorDbContext> DeclareAsync(params NhlSeason[] seasons)
    {
        var db = SqlFixture.NewContext();
        db.Seasons.AddRange(seasons);
        await db.SaveChangesAsync();
        return db;
    }

    private static NhlSeason Declared(string season, string start, string end) => new()
    {
        Season = season,
        RegularSeasonStart = DateOnly.Parse(start),
        RegularSeasonEnd = DateOnly.Parse(end),
    };

    [SqlFact]
    public async Task DeclaredWindow_IsNullForASeasonNobodyDeclared()
    {
        await using var db = SqlFixture.NewContext();

        Assert.Null(await SeasonLookup.DeclaredWindowAsync(db, "20892090"));
    }

    [SqlFact]
    public async Task DeclaredWindow_ReturnsTheRegularSeasonSpan()
    {
        await using var db = await DeclareAsync(Declared("20302031", "2030-10-08", "2031-04-14"));

        var window = await SeasonLookup.DeclaredWindowAsync(db, "20302031");

        Assert.Equal(new DateOnly(2030, 10, 8), window!.Value.Start);
        Assert.Equal(new DateOnly(2031, 4, 14), window.Value.End);
    }

    [SqlFact]
    public async Task CurrentOn_ADayInsideASeason_IsThatSeason()
    {
        await using var db = await DeclareAsync(Declared("20312032", "2031-10-07", "2032-04-12"));

        Assert.Equal("20312032", await SeasonLookup.CurrentOnAsync(db, new DateOnly(2031, 12, 22)));
    }

    [SqlFact]
    public async Task CurrentOn_IncludesBothEndpoints()
    {
        await using var db = await DeclareAsync(Declared("20322033", "2032-10-05", "2033-04-11"));

        Assert.Equal("20322033", await SeasonLookup.CurrentOnAsync(db, new DateOnly(2032, 10, 5)));
        Assert.Equal("20322033", await SeasonLookup.CurrentOnAsync(db, new DateOnly(2033, 4, 11)));
    }

    [SqlFact]
    public async Task CurrentOn_ADayInTheOffSeason_IsTheSeasonAboutToStart()
    {
        // July: the season that just ended is over and done with, and every job
        // asking "which season" in July means the one being prepared.
        await using var db = await DeclareAsync(
            Declared("20332034", "2033-10-04", "2034-04-10"),
            Declared("20342035", "2034-10-03", "2035-04-09"));

        Assert.Equal("20342035", await SeasonLookup.CurrentOnAsync(db, new DateOnly(2034, 7, 15)));
    }

    [SqlFact]
    public async Task CurrentOn_PastEverySeasonWeKnow_IsNull()
    {
        // Not an error and not a guess — the caller decides what to do, and
        // that decision is CurrentOrGuessAsync's.
        await using var db = SqlFixture.NewContext();

        Assert.Null(await SeasonLookup.CurrentOnAsync(db, new DateOnly(2099, 6, 1)));
    }

    [SqlFact]
    public async Task CurrentOrGuess_FallsBackToTheSeptemberCutover()
    {
        await using var db = SqlFixture.NewContext();
        var day = new DateOnly(2099, 6, 1);

        Assert.Equal(CoreSeason.CurrentOn(day), await SeasonLookup.CurrentOrGuessAsync(db, day));
    }

    [SqlFact]
    public async Task CurrentOrGuess_PrefersTheDeclaredAnswerOverTheGuess()
    {
        // The heuristic would answer 20352036 for a day in August 2035; the
        // declared calendar says the season does not open until October, so the
        // two disagree and the table has to win.
        await using var db = await DeclareAsync(Declared("20352036", "2035-10-02", "2036-04-08"));
        var day = new DateOnly(2035, 8, 20);

        Assert.Equal("20352036", await SeasonLookup.CurrentOrGuessAsync(db, day));
    }
}
