namespace FantasyWarrior.Core.Rules;

/// <summary>
/// Every rule one league plays by, for one season. The whole configuration
/// surface in one document.
///
/// <b>Why one document rather than columns.</b> Rules were ten columns on
/// <c>Leagues</c> plus a <c>LeagueScoringRules</c> table, and both mutated in
/// place: a lifetime pool could not answer "what were season 2's rules?", and
/// every new parameter was a migration. This lives on a <c>LeagueSeason</c>, one
/// version per season, so the history is a side effect of where it is stored
/// rather than a feature somebody has to maintain.
///
/// <b>Two documents are live at once during the off-season, and that is
/// correct.</b> The season being *scored* (<c>League.Season</c>) and the season
/// being *prepared* (the one <c>LeagueSeason</c> row that is not
/// <c>Complete</c>) are different rows in July: the standings still pay under
/// last season's scale while the draft runs under next season's protection
/// rules. Callers say which they mean.
///
/// <b>Not every value here is enforced.</b> The catalogue is deliberately
/// complete — a commissioner can record the pool's real rules today, and
/// <see cref="RuleSetCapabilities"/> reports which of them the code actually
/// honours so the UI can say so. What must never happen is the middle ground
/// this replaces: a value stored, ignored, and quietly replaced by a default.
/// Where a consumer meets a value it cannot honour it refuses the action.
/// </summary>
public sealed class RuleSet
{
    /// <summary>
    /// The shape of this document, not the league's season. Bumped only when a
    /// stored document needs converting on read.
    ///
    /// <b>Zero means never written</b>, and that is load-bearing: the column
    /// defaults to <c>'{}'</c>, which deserializes to every property's default —
    /// indistinguishable from a league that genuinely plays the defaults unless
    /// something separates them. A league whose rules were never converted must
    /// not quietly read as one with no cap and no slots, so
    /// <see cref="IsUnwritten"/> is what callers refuse on. Only
    /// <see cref="RuleSetDefaults.ForNewLeague"/> and a real save set this.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Has anyone ever written this document? A stored <c>'{}'</c> has not, and
    /// no rule may be read off it.
    /// </summary>
    public bool IsUnwritten => Version < 1;

    public PoolType PoolType { get; set; } = PoolType.Keeper;

    public CapConfig Cap { get; set; } = new();

    public RosterConfig Roster { get; set; } = new();

    public LineupConfig Lineup { get; set; } = new();

    public ScoringConfig Scoring { get; set; } = new();

    public TradeConfig Trades { get; set; } = new();

    public ProtectionConfig Protection { get; set; } = new();

    public DraftConfig Draft { get; set; } = new();

    public FreeAgencyConfig FreeAgency { get; set; } = new();
}

/// <summary>
/// What happens between two seasons.
///
/// <b>Points resetting each season is a property of <see cref="Keeper"/> as
/// implemented</b>, not a separate setting: <c>vStandings</c> filters
/// <c>RosterAssignments</c> by the league's current season, so a keeper roster
/// carries over while its totals start at zero. Les Mordus calls itself a "pool
/// à vie" and plays exactly that way. A pool that accumulates points for life
/// would be a <b>third value here</b>, never a second field — the two would then
/// be independent, and nothing in the app is written for that yet.
/// </summary>
public enum PoolType
{
    /// <summary>Rosters carry over; protections and a steal draft are what the off-season is for.</summary>
    Keeper,

    /// <summary>Every season starts from an empty roster and a full draft.</summary>
    SingleSeason,
}

/// <summary>The salary cap, and what an unsigned player costs against it.</summary>
public sealed class CapConfig
{
    /// <summary>Ceiling in whole dollars. Null = the league has no cap.</summary>
    public long? Max { get; set; }

    /// <summary>
    /// Floor in whole dollars. Null = no floor, which is a real choice and not
    /// a floor of zero.
    /// </summary>
    public long? Min { get; set; }

    /// <summary>
    /// What a player with no contract on file costs against the cap.
    ///
    /// "No contract" is not a data gap to wait out — it is the permanent,
    /// ordinary state of an unsigned free agent and of a drafted prospect, and a
    /// keeper pool holds plenty of both. Not nullable: every league needs an
    /// answer, and null would only be "$0" spelled at greater length. Set it to
    /// 0 to carry them free.
    /// </summary>
    public long DefaultCapHit { get; set; } = 1_000_000;
}

/// <summary>How many players a team holds, and whether it also holds a franchise.</summary>
public sealed class RosterConfig
{
    public int? Min { get; set; }

    public int? Max { get; set; }

    /// <summary>
    /// Bounds per position group, on top of the overall ones. Null entries mean
    /// the league does not constrain that group.
    /// </summary>
    public PositionBounds ByPosition { get; set; } = new();

    /// <summary>
    /// Does every team own one NHL franchise that scores — the Équipe slot, a
    /// <c>RosterSpot</c> of group <c>T</c>, at most one per team.
    ///
    /// It costs no cap and fills no player slot, and it can only be traded
    /// against another franchise. When false, the <c>team*</c> scoring keys have
    /// nothing to pay.
    /// </summary>
    public bool FranchiseSlot { get; set; }
}

/// <summary>Which players score in a given week.</summary>
public sealed class LineupConfig
{
    /// <summary>
    /// How the week's scorers are chosen. The two modes use the same per-group
    /// counts and mean entirely different things — see <see cref="LineupMode"/>.
    /// </summary>
    public LineupMode Mode { get; set; } = LineupMode.ActiveSelection;

    /// <summary>Slots per position group. Fielding fewer is allowed; more is refused.</summary>
    public PositionCounts Slots { get; set; } = new();

    /// <summary>What happens when a GM submits no lineup for a week.</summary>
    public MissingLineupBehaviour OnMissing { get; set; } = MissingLineupBehaviour.CarryForward;
}

/// <summary>
/// The two ways a week's scorers are decided — one decision that used to be
/// fused into a single <c>topCount</c> setting whose own documentation and UI
/// label disagreed about which it meant.
/// </summary>
public enum LineupMode
{
    /// <summary>
    /// The GM activates a subset of his roster before the week locks, and only
    /// those players score. What Les Mordus play, and the whole reason lineups
    /// lock on Monday.
    /// </summary>
    ActiveSelection,

    /// <summary>
    /// The best N per position group score automatically, with nothing to
    /// submit and nothing to lock.
    /// </summary>
    TopN,
}

/// <summary>What a forgotten lineup costs.</summary>
public enum MissingLineupBehaviour
{
    /// <summary>
    /// Last week's actives carry forward, minus whoever left, topped up with the
    /// best available. A GM on vacation is not punished — in a pool between
    /// friends that would drain the standings of meaning.
    /// </summary>
    CarryForward,

    /// <summary>Nobody is active, and the team scores nothing that week.</summary>
    ScoreZero,
}

/// <summary>What each statistic pays.</summary>
public sealed class ScoringConfig
{
    /// <summary>
    /// The scale, keyed by <c>StatKeys</c> name. A map rather than a fixed list
    /// is what lets a commissioner score blocked shots or hits with no schema
    /// change; an unknown key is rejected rather than absorbed, because it would
    /// score zero forever and read as a calculation bug rather than a typo.
    /// </summary>
    public Dictionary<string, double> Values { get; set; } = [];

    /// <summary>
    /// Per-position-group overrides, keyed by <c>F</c>/<c>D</c>/<c>G</c> and
    /// then by stat — "a defenceman's goal is worth two". A group absent here
    /// scores under <see cref="Values"/>, and a stat absent from a present group
    /// falls back to it too, so an override names only what differs.
    /// </summary>
    public Dictionary<string, Dictionary<string, double>> ByPosition { get; set; } = [];

    /// <summary>
    /// Do playoff games score. False everywhere today — <c>GameType == 2</c> is
    /// filtered in the rollup job, the views and the API reads alike — which is
    /// a rule of the pool that until now had no name.
    /// </summary>
    public bool IncludePlayoffs { get; set; }

    /// <summary>
    /// The scale one position group actually scores under: <see cref="Values"/>
    /// with that group's overrides laid on top. The only form the scoring engine
    /// consumes, so no caller has to know whether a value came from the general
    /// scale or a group's exception.
    ///
    /// Returns a fresh dictionary rather than a view — callers mutate their copy
    /// freely, and no override can reach back into the shared scale.
    /// </summary>
    public Dictionary<string, double> ScaleFor(string positionGroup)
    {
        var scale = new Dictionary<string, double>(Values);
        if (ByPosition.TryGetValue(positionGroup, out var overrides))
            foreach (var (key, value) in overrides) scale[key] = value;
        return scale;
    }
}

/// <summary>What may change hands, and whether anyone has to agree.</summary>
public sealed class TradeConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>May draft picks be included in a trade.</summary>
    public bool PicksTradable { get; set; } = true;

    /// <summary>
    /// How many seasons ahead picks exist and can therefore be traded. One
    /// today: <c>draft-picks-init</c> generates a single season's picks, which
    /// is what makes "tradable a year in advance" true without a rule saying so.
    /// </summary>
    public int PickYearsAhead { get; set; } = 1;

    /// <summary>Who, if anyone, has to sign off before a trade executes.</summary>
    public TradeApproval Approval { get; set; } = TradeApproval.None;
}

/// <summary>
/// Who can stop a trade. <c>TradeVotes</c> exists today but is advisory — it
/// feeds the community trade rating and blocks nothing.
/// </summary>
public enum TradeApproval
{
    /// <summary>Both GMs agreeing is the whole process.</summary>
    None,

    /// <summary>The commissioner may veto an accepted trade before it executes.</summary>
    Commissioner,

    /// <summary>The league votes, and enough objections stop it.</summary>
    LeagueVote,
}

/// <summary>Who a GM may shelter from the off-season steal draft.</summary>
public sealed class ProtectionConfig
{
    /// <summary>
    /// Roster spots a GM may protect. Null = the league has no protection rule
    /// at all, which is not the same as zero.
    /// </summary>
    public int? Slots { get; set; }

    /// <summary>
    /// Slots per position group, when a league caps them separately — "four
    /// forwards, three defencemen, one goalie". Null entries mean that group is
    /// bounded only by <see cref="Slots"/>.
    /// </summary>
    public PositionCounts? SlotsByPosition { get; set; }

    public AutoProtectConfig Auto { get; set; } = new();

    /// <summary>What becomes of an exposed player nobody claimed.</summary>
    public AfterDraftDisposition AfterDraft { get; set; } = AfterDraftDisposition.StayWithTeam;
}

/// <summary>
/// Protection nobody has to pay for: too little NHL experience and a player is
/// out of reach, which is what stops a pool becoming a prospect raid every
/// summer.
///
/// <b>The measurement is stored and the verdict derived</b> —
/// <c>Player.CareerNhlGames</c> against these bars — so moving a threshold
/// rewrites no rows. <b>Goalies count separately, and lower</b>, because a
/// goalie plays roughly half his club's games and would otherwise stay
/// untouchable for twice as many seasons.
/// </summary>
public sealed class AutoProtectConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>A forward or defenceman at or under this many career NHL games cannot be drafted away.</summary>
    public int SkaterMaxCareerGames { get; set; } = 100;

    /// <summary>A goalie at or under this many career NHL games cannot be drafted away.</summary>
    public int GoalieMaxCareerGames { get; set; } = 50;
}

/// <summary>Where an unclaimed exposed player ends up once the draft closes.</summary>
public enum AfterDraftDisposition
{
    /// <summary>He stays on the roster he was exposed from, having never moved.</summary>
    StayWithTeam,

    /// <summary>He is released, and anyone may sign him through free agency.</summary>
    ReleasedToFreeAgents,
}

/// <summary>The off-season draft: its steal segment, and its rookie segment.</summary>
public sealed class DraftConfig
{
    /// <summary>How an unprotected player becomes available to other GMs.</summary>
    public UnprotectedDisposition UnprotectedDisposition { get; set; } = UnprotectedDisposition.StealRounds;

    public StealConfig Steal { get; set; } = new();

    /// <summary>
    /// Rookie / free-agent rounds, generating one <c>DraftPicks</c> row per team
    /// per round — so this is also "picks per team per year". Null or 0 = no
    /// such segment.
    ///
    /// Deliberately independent of <see cref="StealConfig.Rounds"/>: they size
    /// two different drafts that run back to back in one room, and seeing them
    /// diverge is not an error.
    /// </summary>
    public int? RookieRounds { get; set; }

    /// <summary>
    /// Does the order reverse every round. False = linear, the same reverse-
    /// standings order each round, which is what the draft room runs today.
    /// </summary>
    public bool Snake { get; set; }
}

/// <summary>How unprotected players reach other GMs.</summary>
public enum UnprotectedDisposition
{
    /// <summary>
    /// Dedicated opening rounds in which a GM takes an exposed player off a
    /// rival's roster. Unprotected players are not otherwise available.
    /// </summary>
    StealRounds,

    /// <summary>
    /// No steal segment: unprotected players simply join the ordinary draft
    /// pool alongside the unrostered ones.
    /// </summary>
    OpenPool,
}

/// <summary>The steal segment's own three numbers.</summary>
public sealed class StealConfig
{
    /// <summary>
    /// Rounds, so also steals per team. Generates no rows: every team gets
    /// exactly this many and there is nothing to own.
    /// </summary>
    public int Rounds { get; set; }

    /// <summary>
    /// May a steal turn be traded. False today, and that is what makes
    /// <c>DraftSelections</c> necessary: with no entitlement row to claim, its
    /// unique index on <c>(LeagueSeasonId, OverallIndex)</c> is the only thing
    /// stopping two GMs from taking turn 7.
    /// </summary>
    public bool TurnsTradable { get; set; }

    /// <summary>
    /// The most players one team may lose across a whole steal draft. Null =
    /// uncapped, a real choice and not a limit of zero.
    ///
    /// It closes the pool from underneath as the draft runs — once a team
    /// reaches this, every remaining player it holds stops being available to
    /// everyone — which is why the available list is recomputed each turn and
    /// never cached.
    /// </summary>
    public int? MaxLossesPerTeam { get; set; }
}

/// <summary>Signing and dropping players outside the draft.</summary>
public sealed class FreeAgencyConfig
{
    public FreeAgencyMode Mode { get; set; } = FreeAgencyMode.None;

    /// <summary>Which half of a move is allowed. Irrelevant while <see cref="Mode"/> is None.</summary>
    public FreeAgencyMoves Allow { get; set; } = FreeAgencyMoves.Both;

    /// <summary>Moves a team may make per scoring week. Null = unlimited.</summary>
    public int? MovesPerPeriod { get; set; }

    /// <summary>
    /// The windows free agency is open in, for <see cref="FreeAgencyMode.Windows"/>.
    /// Ignored in the other modes.
    /// </summary>
    public List<FreeAgencyWindow> Windows { get; set; } = [];
}

/// <summary>When a team may add or drop.</summary>
public enum FreeAgencyMode
{
    /// <summary>Not at all. A roster changes only by trade and by draft.</summary>
    None,

    /// <summary>Whenever, subject to <see cref="FreeAgencyConfig.MovesPerPeriod"/>.</summary>
    Anytime,

    /// <summary>Only inside the declared <see cref="FreeAgencyConfig.Windows"/>.</summary>
    Windows,
}

/// <summary>Which half of a free-agency move a league permits.</summary>
public enum FreeAgencyMoves
{
    Add,
    Drop,
    Both,
}

/// <summary>One named period during which free agency is open. Inclusive dates.</summary>
public sealed class FreeAgencyWindow
{
    public string Name { get; set; } = "";

    public DateOnly Start { get; set; }

    public DateOnly End { get; set; }
}

/// <summary>
/// A count per position group. <c>T</c> is deliberately absent: a team holds at
/// most one franchise, guaranteed by a unique index, so there is no count to
/// configure.
/// </summary>
public sealed class PositionCounts
{
    public int Forwards { get; set; }

    public int Defense { get; set; }

    public int Goalies { get; set; }

    /// <summary>By the persisted group letter — F, D or G.</summary>
    public int For(string positionGroup) => positionGroup switch
    {
        "D" => Defense,
        "G" => Goalies,
        _ => Forwards,
    };

    public int Total => Forwards + Defense + Goalies;
}

/// <summary>Optional min/max per position group; null means the league does not say.</summary>
public sealed class PositionBounds
{
    public SizeBounds Forwards { get; set; } = new();

    public SizeBounds Defense { get; set; } = new();

    public SizeBounds Goalies { get; set; } = new();

    public SizeBounds For(string positionGroup) => positionGroup switch
    {
        "D" => Defense,
        "G" => Goalies,
        _ => Forwards,
    };

    /// <summary>True when no group constrains anything — the ordinary case.</summary>
    public bool IsEmpty => Forwards.IsEmpty && Defense.IsEmpty && Goalies.IsEmpty;
}

/// <summary>A min/max pair where null means "no bound", never zero.</summary>
public sealed class SizeBounds
{
    public int? Min { get; set; }

    public int? Max { get; set; }

    public bool IsEmpty => Min is null && Max is null;
}
