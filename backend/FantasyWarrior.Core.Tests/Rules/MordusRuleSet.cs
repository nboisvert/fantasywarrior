using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Rules;

/// <summary>
/// Les Mordus' real rules, as a <see cref="RuleSet"/>.
///
/// The one configuration that has to be completely supported: it is the pool the
/// app is built for, so a gap reported against it is a feature that has to ship
/// before October, not a badge. The numbers themselves are documented in
/// <c>mordus.md</c>, which stays their single source — this exists so a change
/// there that the code cannot honour fails a test rather than a season.
/// </summary>
public static class MordusRuleSet
{
    public static RuleSet Build() => new()
    {
        PoolType = PoolType.Keeper,

        Cap = new CapConfig { Max = 134_000_000, Min = null, DefaultCapHit = 1_000_000 },

        Roster = new RosterConfig { Min = 23, Max = 35, FranchiseSlot = true },

        Lineup = new LineupConfig
        {
            Mode = LineupMode.ActiveSelection,
            Slots = new PositionCounts { Forwards = 9, Defense = 4, Goalies = 1 },
            OnMissing = MissingLineupBehaviour.CarryForward,
        },

        Scoring = new ScoringConfig
        {
            Values = new Dictionary<string, double>
            {
                [StatKeys.Goals] = 1,
                [StatKeys.Assists] = 1,
                [StatKeys.Wins] = 2,
                [StatKeys.OtLosses] = 1,
                [StatKeys.Shutouts] = 0,
                [StatKeys.TeamWins] = 2,
                [StatKeys.TeamOtLosses] = 1,
                [StatKeys.TeamLosses] = 0,
            },
        },

        Trades = new TradeConfig { Enabled = true, PicksTradable = true, PickYearsAhead = 1 },

        Protection = new ProtectionConfig
        {
            Slots = 9,
            Auto = new AutoProtectConfig
            {
                Enabled = true,
                SkaterMaxCareerGames = 100,
                GoalieMaxCareerGames = 50,
            },
            AfterDraft = AfterDraftDisposition.StayWithTeam,
        },

        Draft = new DraftConfig
        {
            UnprotectedDisposition = UnprotectedDisposition.StealRounds,
            Steal = new StealConfig { Rounds = 2, TurnsTradable = false, MaxLossesPerTeam = 2 },
            RookieRounds = 3,
            Snake = false,
        },

        FreeAgency = new FreeAgencyConfig { Mode = FreeAgencyMode.None },
    };
}
