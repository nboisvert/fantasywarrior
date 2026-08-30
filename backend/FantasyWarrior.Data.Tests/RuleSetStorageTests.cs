using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Leagues;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// The rules document against a real SQL Server: the value converter, the
/// comparer that makes an edit detectable, and the three ways of asking which
/// season's rules are meant.
/// </summary>
[Collection(SqlCollection.Name)]
public class RuleSetStorageTests
{
    private static async Task<(FantasyWarriorDbContext Db, TestWorld World)> WorldAsync()
    {
        var db = SqlFixture.NewContext();
        var world = await new TestWorld(db).CreateAsync();
        return (db, world);
    }

    private static async Task<LeagueSeason> AddSeasonAsync(
        FantasyWarriorDbContext db, int leagueId, string season, int number,
        LeagueSeasonPhase phase, RuleSet? rules = null)
    {
        var row = new LeagueSeason
        {
            LeagueId = leagueId,
            Season = season,
            Number = number,
            Phase = phase,
            StartedUtc = DateTime.UtcNow,
        };
        if (rules is not null) row.Rules = rules;
        db.LeagueSeasons.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    [SqlFact]
    public async Task ARuleSetSurvivesTheRoundTripThroughSqlServer()
    {
        var (db, world) = await WorldAsync();
        await using var _ = db;

        var row = await AddSeasonAsync(
            db, world.League.LeagueId, world.League.Season, 1,
            LeagueSeasonPhase.InSeason, MordusRules());

        db.ChangeTracker.Clear();
        var read = await db.LeagueSeasons.AsNoTracking()
            .FirstAsync(s => s.LeagueSeasonId == row.LeagueSeasonId);

        Assert.Equal(134_000_000, read.Rules.Cap.Max);
        Assert.Equal(9, read.Rules.Lineup.Slots.Forwards);
        Assert.Equal(2, read.Rules.Draft.Steal.Rounds);
        Assert.Equal(2, read.Rules.Scoring.Values[StatKeys.TeamWins]);
        Assert.True(read.Rules.Roster.FranchiseSlot);
    }

    [SqlFact]
    public async Task EditingTheGraphInPlaceIsDetectedAndSaved()
    {
        // The failure the ValueComparer exists to prevent: RuleSet is a mutable
        // graph, so reference equality would compare a tracked entity to itself
        // and conclude nothing changed. Every rules edit would be dropped at
        // SaveChanges with no error anywhere.
        var (db, world) = await WorldAsync();
        await using var _ = db;

        var row = await AddSeasonAsync(
            db, world.League.LeagueId, world.League.Season, 1,
            LeagueSeasonPhase.InSeason, MordusRules());

        row.Rules.Protection.Slots = 11;
        row.Rules.Scoring.Values[StatKeys.Hits] = 0.5;
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var read = await db.LeagueSeasons.AsNoTracking()
            .FirstAsync(s => s.LeagueSeasonId == row.LeagueSeasonId);

        Assert.Equal(11, read.Rules.Protection.Slots);
        Assert.Equal(0.5, read.Rules.Scoring.Values[StatKeys.Hits]);
    }

    [SqlFact]
    public async Task TheColumnDefaultReadsAsUnwritten()
    {
        // What every existing LeagueSeason looks like between the migration that
        // adds the column and rules-backfill.
        var (db, world) = await WorldAsync();
        await using var _ = db;

        var row = await AddSeasonAsync(
            db, world.League.LeagueId, world.League.Season, 1, LeagueSeasonPhase.InSeason);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE LeagueSeasons SET Rules = '{{}}' WHERE LeagueSeasonId = {row.LeagueSeasonId}");

        db.ChangeTracker.Clear();
        var read = await db.LeagueSeasons.AsNoTracking()
            .FirstAsync(s => s.LeagueSeasonId == row.LeagueSeasonId);

        Assert.True(read.Rules.IsUnwritten);
    }

    [SqlFact]
    public async Task AnUnwrittenDocumentIsRefused_NotServedAsDefaults()
    {
        // Serving it would report "no cap, no slots, no protections" as the
        // league's rules, and it would look exactly like a correctly configured
        // permissive league.
        var (db, world) = await WorldAsync();
        await using var _ = db;

        var row = await AddSeasonAsync(
            db, world.League.LeagueId, world.League.Season, 1, LeagueSeasonPhase.InSeason);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE LeagueSeasons SET Rules = '{{}}' WHERE LeagueSeasonId = {row.LeagueSeasonId}");
        db.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<RuleSetUnavailableException>(
            () => RuleSetResolver.ForScoringAsync(db, world.League));

        Assert.Contains("rules-backfill", error.Message);
    }

    [SqlFact]
    public async Task ForScoringReadsTheSeasonBeingPlayed_NotTheOneBeingPrepared()
    {
        // The off-season shape: last season is Complete and still what the
        // standings pay under, while next season sits in Protecting under its
        // own rules. A single "get the rules" method would silently pick one.
        var (db, world) = await WorldAsync();
        await using var _ = db;

        var played = MordusRules();
        var preparing = MordusRules();
        preparing.Protection.Slots = 12;
        preparing.Scoring.Values[StatKeys.Goals] = 3;

        await AddSeasonAsync(
            db, world.League.LeagueId, world.League.Season, 3, LeagueSeasonPhase.Complete, played);
        await AddSeasonAsync(
            db, world.League.LeagueId, Season.Next(world.League.Season), 4,
            LeagueSeasonPhase.Protecting, preparing);
        db.ChangeTracker.Clear();

        var scoring = await RuleSetResolver.ForScoringAsync(db, world.League);
        var active = await RuleSetResolver.ForActiveSeasonAsync(db, world.League.LeagueId);

        Assert.Equal(9, scoring.Protection.Slots);
        Assert.Equal(1, scoring.Scoring.Values[StatKeys.Goals]);
        Assert.Equal(12, active.Protection.Slots);
        Assert.Equal(3, active.Scoring.Values[StatKeys.Goals]);
    }

    [SqlFact]
    public async Task ForSeasonAnswersTheHistoricalQuestion()
    {
        // "What were season 3's rules?" — the thing a lifetime pool could not
        // answer while rules mutated in place on the league.
        var (db, world) = await WorldAsync();
        await using var _ = db;

        var old = MordusRules();
        old.Cap.Max = 115_000_000;
        await AddSeasonAsync(
            db, world.League.LeagueId, world.League.Season, 3, LeagueSeasonPhase.Complete, old);
        await AddSeasonAsync(
            db, world.League.LeagueId, Season.Next(world.League.Season), 4,
            LeagueSeasonPhase.InSeason, MordusRules());
        db.ChangeTracker.Clear();

        var third = await RuleSetResolver.ForSeasonAsync(
            db, world.League.LeagueId, world.League.Season);

        Assert.Equal(115_000_000, third.Cap.Max);
    }

    [SqlFact]
    public async Task ForEditingReturnsTheSeasonBeingPrepared_AndSavesThroughIt()
    {
        // Editing a Complete season would restate what a finished season was
        // played under, which is the one thing storing rules per season prevents.
        var (db, world) = await WorldAsync();
        await using var _ = db;

        await AddSeasonAsync(
            db, world.League.LeagueId, world.League.Season, 3, LeagueSeasonPhase.Complete, MordusRules());
        var preparing = await AddSeasonAsync(
            db, world.League.LeagueId, Season.Next(world.League.Season), 4,
            LeagueSeasonPhase.Protecting, MordusRules());
        db.ChangeTracker.Clear();

        var target = await RuleSetResolver.ForEditingAsync(db, world.League.LeagueId);
        target.Rules.Cap.Max = 140_000_000;
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        Assert.Equal(preparing.LeagueSeasonId, target.LeagueSeasonId);
        Assert.Equal(
            140_000_000,
            (await RuleSetResolver.ForActiveSeasonAsync(db, world.League.LeagueId)).Cap.Max);
        Assert.Equal(
            134_000_000,
            (await RuleSetResolver.ForSeasonAsync(db, world.League.LeagueId, world.League.Season)).Cap.Max);
    }

    [SqlFact]
    public async Task ALeagueWithNoOpenSeasonIsRefusedWithTheCommandToFixIt()
    {
        var (db, world) = await WorldAsync();
        await using var _ = db;

        await AddSeasonAsync(
            db, world.League.LeagueId, world.League.Season, 3, LeagueSeasonPhase.Complete, MordusRules());
        db.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<RuleSetUnavailableException>(
            () => RuleSetResolver.ForActiveSeasonAsync(db, world.League.LeagueId));

        Assert.Contains("season-phase", error.Message);
    }

    private static RuleSet MordusRules()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Cap.Max = 134_000_000;
        rules.Roster.Min = 23;
        rules.Roster.Max = 35;
        rules.Roster.FranchiseSlot = true;
        rules.Lineup.Slots = new PositionCounts { Forwards = 9, Defense = 4, Goalies = 1 };
        rules.Scoring.Values[StatKeys.TeamWins] = 2;
        rules.Scoring.Values[StatKeys.TeamOtLosses] = 1;
        rules.Scoring.Values[StatKeys.TeamLosses] = 0;
        rules.Protection.Slots = 9;
        rules.Draft.Steal.Rounds = 2;
        rules.Draft.Steal.MaxLossesPerTeam = 2;
        rules.Draft.RookieRounds = 3;
        return rules;
    }
}
