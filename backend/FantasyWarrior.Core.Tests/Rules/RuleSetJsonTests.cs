using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Rules;

/// <summary>
/// The serializer settings are the storage format, so these are schema tests.
/// </summary>
public class RuleSetJsonTests
{
    [Fact]
    public void LesMordusRulesSurviveARoundTrip()
    {
        var back = RuleSetJson.Deserialize(RuleSetJson.Serialize(MordusRuleSet.Build()));
        var original = MordusRuleSet.Build();

        Assert.Equal(original.PoolType, back.PoolType);
        Assert.Equal(original.Cap.Max, back.Cap.Max);
        Assert.Equal(original.Cap.DefaultCapHit, back.Cap.DefaultCapHit);
        Assert.Equal(original.Roster.Min, back.Roster.Min);
        Assert.Equal(original.Roster.Max, back.Roster.Max);
        Assert.True(back.Roster.FranchiseSlot);
        Assert.Equal(9, back.Lineup.Slots.Forwards);
        Assert.Equal(4, back.Lineup.Slots.Defense);
        Assert.Equal(1, back.Lineup.Slots.Goalies);
        Assert.Equal(original.Scoring.Values, back.Scoring.Values);
        Assert.Equal(9, back.Protection.Slots);
        Assert.Equal(100, back.Protection.Auto.SkaterMaxCareerGames);
        Assert.Equal(50, back.Protection.Auto.GoalieMaxCareerGames);
        Assert.Equal(2, back.Draft.Steal.Rounds);
        Assert.Equal(2, back.Draft.Steal.MaxLossesPerTeam);
        Assert.Equal(3, back.Draft.RookieRounds);
        Assert.Equal(FreeAgencyMode.None, back.FreeAgency.Mode);
    }

    [Fact]
    public void EnumsAreWrittenAsCamelCaseNames()
    {
        // The point of a JSON column over a wall of columns is that a human can
        // read the rules straight out of the database — and a numeric enum would
        // silently repoint every stored value the day one is reordered.
        var json = RuleSetJson.Serialize(MordusRuleSet.Build());

        Assert.Contains("\"poolType\":\"keeper\"", json);
        Assert.Contains("\"mode\":\"activeSelection\"", json);
        Assert.Contains("\"unprotectedDisposition\":\"stealRounds\"", json);
        Assert.DoesNotContain("\"poolType\":0", json);
    }

    [Fact]
    public void PropertiesAreCamelCasedButStatKeysAreNot()
    {
        // Stat names and position letters are data, not property names: a
        // naming policy applied to dictionary keys would rewrite "teamOtLosses"
        // and the scale would stop matching StatKeys.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Scoring.Values[StatKeys.TeamOtLosses] = 1;
        rules.Scoring.ByPosition["D"] = new Dictionary<string, double> { [StatKeys.Goals] = 2 };

        var json = RuleSetJson.Serialize(rules);

        Assert.Contains("\"defaultCapHit\"", json);
        Assert.Contains("\"teamOtLosses\":1", json);
        Assert.Contains("\"D\":{", json);
    }

    [Fact]
    public void AnEmptyDocumentReadsAsANewLeaguesDefaults()
    {
        // The column defaults to '{}', so this is what every LeagueSeason looks
        // like between the migration that adds the column and the one that
        // fills it.
        foreach (var stored in new[] { "{}", "", "   ", null })
        {
            var rules = RuleSetJson.Deserialize(stored);

            Assert.Equal(RuleSetDefaults.CurrentVersion, rules.Version);
            Assert.Equal(RuleSetDefaults.StartingScale(), rules.Scoring.Values);
        }
    }

    [Fact]
    public void APropertyMissingFromAStoredDocumentTakesItsDefault()
    {
        // A document written before a rule existed must keep playing what the
        // code played then, which is that rule's default.
        var rules = RuleSetJson.Deserialize("""{"version":1,"cap":{"max":134000000}}""");

        Assert.Equal(134_000_000, rules.Cap.Max);
        Assert.Equal(1_000_000, rules.Cap.DefaultCapHit);
        Assert.Equal(PoolType.Keeper, rules.PoolType);
        Assert.Equal(MissingLineupBehaviour.CarryForward, rules.Lineup.OnMissing);
        Assert.Equal(100, rules.Protection.Auto.SkaterMaxCareerGames);
    }

    [Fact]
    public void ARoundTrippedDocumentIsByteIdenticalTheSecondTime()
    {
        // A PATCH that reads, changes nothing and writes back must not churn the
        // stored document, or every save would look like a rule change.
        var once = RuleSetJson.Serialize(MordusRuleSet.Build());
        var twice = RuleSetJson.Serialize(RuleSetJson.Deserialize(once));

        Assert.Equal(once, twice);
    }
}
