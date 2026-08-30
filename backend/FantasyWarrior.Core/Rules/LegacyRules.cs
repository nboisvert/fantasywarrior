using FantasyWarrior.Core.Drafts;

namespace FantasyWarrior.Core.Rules;

/// <summary>
/// A league's rules in the shape they were stored in before <see cref="RuleSet"/>:
/// ten columns on <c>Leagues</c> plus the rows of <c>LeagueScoringRules</c>.
///
/// A record rather than eleven positional parameters, so the one call site that
/// matters — the data migration that fills every <c>LeagueSeason</c> — reads as
/// the mapping it is.
/// </summary>
/// <param name="HasFranchiseSlots">
/// Whether the league actually holds Équipe (<c>T</c>) roster spots. Not a
/// column anywhere: it was never a setting, only a consequence of how a league
/// was seeded, so the migration reads it off <c>RosterSpots</c> and passes the
/// answer in.
/// </param>
public sealed record LegacyLeagueRules(
    long? CapAmount,
    long DefaultCapHit,
    int? RosterMin,
    int? RosterMax,
    int ActiveForwards,
    int ActiveDefense,
    int ActiveGoalies,
    int? DraftRounds,
    int? ProtectionSlots,
    int? StealRounds,
    int? MaxLossesPerTeam,
    bool HasFranchiseSlots,
    IReadOnlyDictionary<string, double> Scale);

/// <summary>
/// Converts the old storage into a <see cref="RuleSet"/>. Pure, so the migration
/// that runs once against production data is tested without a database.
///
/// <b>Everything the old shape could not express takes the value the code was
/// hardcoded to.</b> That is the correct reading: a league that had no way to
/// say "playoffs do not score" was nonetheless playing under that rule, because
/// the filter was in the rollup job. Writing the hardcoded behaviour down is
/// what makes the document true on the day it is written, and what lets the
/// hardcoding be removed afterwards without changing anyone's rules.
///
/// Two of them are assumptions rather than readings, and worth naming:
/// <see cref="PoolType"/> is <c>Keeper</c> because rosters have always carried
/// over and no other kind of league exists here, and every closed season gets
/// today's rules because nothing recorded yesterday's. The history is honest
/// from this point forward, not before it.
/// </summary>
public static class LegacyRules
{
    public static RuleSet ToRuleSet(LegacyLeagueRules legacy) => new()
    {
        Version = RuleSetDefaults.CurrentVersion,
        PoolType = PoolType.Keeper,

        Cap = new CapConfig
        {
            Max = legacy.CapAmount,
            // Never existed as a column: no league has ever had a floor.
            Min = null,
            DefaultCapHit = legacy.DefaultCapHit,
        },

        Roster = new RosterConfig
        {
            Min = legacy.RosterMin,
            Max = legacy.RosterMax,
            ByPosition = new PositionBounds(),
            FranchiseSlot = legacy.HasFranchiseSlots,
        },

        Lineup = new LineupConfig
        {
            // The old topCount fed LineupRules.SlotsFrom, so whatever its own
            // documentation said, what it did was pick the week's actives.
            Mode = LineupMode.ActiveSelection,
            Slots = new PositionCounts
            {
                Forwards = legacy.ActiveForwards,
                Defense = legacy.ActiveDefense,
                Goalies = legacy.ActiveGoalies,
            },
            OnMissing = MissingLineupBehaviour.CarryForward,
        },

        Scoring = new ScoringConfig
        {
            Values = new Dictionary<string, double>(legacy.Scale),
            ByPosition = [],
            // GameType == 2 is filtered in PeriodInitJob, PeriodRollupJob,
            // vPlayerSeasonStats and every API read.
            IncludePlayoffs = false,
        },

        Trades = new TradeConfig
        {
            Enabled = true,
            PicksTradable = true,
            // draft-picks-init generates one season's picks and only one.
            PickYearsAhead = 1,
            // TradeVotes rate a trade; they have never blocked one.
            Approval = TradeApproval.None,
        },

        Protection = new ProtectionConfig
        {
            Slots = legacy.ProtectionSlots,
            SlotsByPosition = null,
            Auto = new AutoProtectConfig
            {
                Enabled = true,
                SkaterMaxCareerGames = ProtectionRules.MaxCareerGamesSkater,
                GoalieMaxCareerGames = ProtectionRules.MaxCareerGamesGoalie,
            },
            AfterDraft = AfterDraftDisposition.StayWithTeam,
        },

        Draft = new DraftConfig
        {
            // The rookie segment has only ever offered unrostered players, so a
            // rostered-but-unprotected player was reachable through the steal
            // rounds or not at all — which is this value, whatever the round
            // count happens to be.
            UnprotectedDisposition = UnprotectedDisposition.StealRounds,
            Steal = new StealConfig
            {
                // Null meant "no steal segment" once it reached `?? 0`. Zero
                // says the same thing without the fall-through that hid it.
                Rounds = legacy.StealRounds ?? 0,
                TurnsTradable = false,
                MaxLossesPerTeam = legacy.MaxLossesPerTeam,
            },
            RookieRounds = legacy.DraftRounds,
            Snake = false,
        },

        FreeAgency = new FreeAgencyConfig
        {
            Mode = FreeAgencyMode.None,
            Allow = FreeAgencyMoves.Both,
            MovesPerPeriod = null,
            Windows = [],
        },
    };
}
