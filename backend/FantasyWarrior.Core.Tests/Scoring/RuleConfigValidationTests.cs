using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Scoring;

public class RuleConfigValidationTests
{
    [Fact]
    public void ADefaultConfigIsValid() => Assert.Empty(RuleConfigValidation.Validate(new RuleConfig()));

    [Fact]
    public void AKnownExtraStatIsAccepted()
    {
        var config = new RuleConfig
        {
            ExtraPointValues = { [StatKeys.BlockedShots] = 0.5, [StatKeys.Hits] = 0.25 },
        };

        Assert.Empty(RuleConfigValidation.Validate(config));
    }

    [Fact]
    public void AnUnknownStatKeyIsRejected()
    {
        // The whole reason this validation exists: a typo would score zero
        // forever and read as a scoring bug rather than a bad config.
        var config = new RuleConfig { ExtraPointValues = { ["goal"] = 1 } };

        var errors = RuleConfigValidation.Validate(config);

        Assert.Single(errors);
        Assert.Contains("Unknown stat \"goal\"", errors[0]);
    }

    [Fact]
    public void NegativeSlotsAreRejected()
    {
        var config = new RuleConfig { TopCount = new TopCount { Forwards = -1 } };

        Assert.Contains(RuleConfigValidation.Validate(config), e => e.Contains("forwards"));
    }

    [Fact]
    public void AnInvertedRosterRangeIsRejected()
    {
        var config = new RuleConfig { RosterSize = new RosterSize { Min = 35, Max = 23 } };

        Assert.Contains(RuleConfigValidation.Validate(config), e => e.Contains("cannot exceed"));
    }

    [Fact]
    public void NullSlotsAndSizesAreFine()
    {
        // Unset is a legitimate state -- it means "no limit configured".
        Assert.Empty(RuleConfigValidation.Validate(new RuleConfig
        {
            TopCount = new TopCount(),
            RosterSize = new RosterSize(),
        }));
    }

    // --- ScoringScale ---

    [Fact]
    public void ScoringScale_ExposesTheFiveFixedValuesUnderStatKeys()
    {
        var scale = new RuleConfig
        {
            PointValues = new PointValues { Goal = 1, Assist = 1, GoalieWin = 1, GoalieOtLoss = 1, Shutout = 0 },
        }.ScoringScale();

        Assert.Equal(1, scale[StatKeys.Goals]);
        Assert.Equal(1, scale[StatKeys.Assists]);
        Assert.Equal(1, scale[StatKeys.Wins]);
        Assert.Equal(1, scale[StatKeys.OtLosses]);
        Assert.Equal(0, scale[StatKeys.Shutouts]);
    }

    [Fact]
    public void ScoringScale_MergesTheExtras()
    {
        var scale = new RuleConfig { ExtraPointValues = { [StatKeys.BlockedShots] = 0.5 } }.ScoringScale();

        Assert.Equal(0.5, scale[StatKeys.BlockedShots]);
        Assert.Equal(1, scale[StatKeys.Goals]);
    }

    [Fact]
    public void ScoringScale_LetsAnExtraOverrideAFixedValue()
    {
        // One way to express a scale the fixed properties can't: the extras win.
        var scale = new RuleConfig { ExtraPointValues = { [StatKeys.Goals] = 3 } }.ScoringScale();

        Assert.Equal(3, scale[StatKeys.Goals]);
    }

    [Fact]
    public void ScoringScale_ScoresTheMordusScaleCorrectly()
    {
        var stats = new StatLine(new Dictionary<string, int>
        {
            [StatKeys.Goals] = 3, [StatKeys.Assists] = 2, [StatKeys.Wins] = 1,
            [StatKeys.OtLosses] = 1, [StatKeys.Shutouts] = 2,
        });
        // Les Mordus: 1/1/1/1, shutouts not scored.
        var config = new RuleConfig
        {
            PointValues = new PointValues { Goal = 1, Assist = 1, GoalieWin = 1, GoalieOtLoss = 1, Shutout = 0 },
        };

        Assert.Equal(7, stats.Score(config.ScoringScale()));
    }
}
