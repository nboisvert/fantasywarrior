namespace FantasyWarrior.Data.Entities;

/// <summary>
/// A pool. The multi-tenancy root: teams, roster spots, trades and draft picks
/// all hang off one.
///
/// <see cref="JoinCode"/> is what the API exposes as the league's <c>id</c>.
/// Under Firestore the document id doubled as the invite code and the frontend
/// keeps it in localStorage; exposing a short code rather than an integer keeps
/// both the join flow and the stored value working untouched.
///
/// Scoring values live in <see cref="ScoringRules"/> (one row per stat) rather
/// than in columns. That is what lets a commissioner score blocked shots or
/// hits without a schema change — the same property the Firestore
/// <c>extraPointValues</c> map had, expressed relationally.
/// </summary>
public sealed class League
{
    public int LeagueId { get; set; }

    public required string Name { get; set; }

    /// <summary>NHL season the league runs on, e.g. "20252026".</summary>
    public required string Season { get; set; }

    /// <summary>Short, unambiguous invite code. Unique; also the public id.</summary>
    public required string JoinCode { get; set; }

    public int CommissionerUserId { get; set; }

    /// <summary>Salary cap per team in whole dollars; null = no cap rule.</summary>
    public long? CapAmount { get; set; }

    /// <summary>
    /// What a player with no contract on file costs against the cap, in whole
    /// dollars. Defaults to $1M (Nick, 2026-08-05).
    ///
    /// "No contract" is not a data gap to wait out — it is a real and permanent
    /// state for an unsigned free agent and for a drafted prospect who has yet
    /// to sign, and a keeper pool holds plenty of both. Counting them at $0, as
    /// this did until now, let a GM carry a dozen of them for free and made
    /// every cap total quietly understated.
    ///
    /// Configurable rather than a constant because it is a league rule, not a
    /// fact: set it to 0 to restore the old behaviour. Not nullable — every
    /// league needs an answer, and "null" would only mean "$0" spelled at
    /// greater length.
    /// </summary>
    public long DefaultCapHit { get; set; } = 1_000_000;

    // --- roster composition. Active-slot counts are enforced at lineup
    // submission; the cap and min/max roster size are enforced on trades since
    // 2026-08-03 -- against the *engaged* figures, which include accepted but
    // unexecuted trades. See scoring-model.md §9.

    public int? RosterMin { get; set; }

    public int? RosterMax { get; set; }

    /// <summary>
    /// Draft rounds generated per season; one pick per team per round, so this
    /// is also "picks per team per year". Les Mordus: 3. Null = no draft.
    ///
    /// One per round is not a simplification but what the schema already
    /// enforces — <c>DraftPicks</c> is unique on
    /// (LeagueId, Year, Round, OriginalTeamId).
    /// </summary>
    public int? DraftRounds { get; set; }

    /// <summary>
    /// How many roster spots a GM may protect before the off-season steal
    /// draft. Null = not configured yet — Les Mordus has never run one, so
    /// there is no real number to default this to (see
    /// <c>mordus-pool.md</c>'s "à fixer" rows). A player who qualifies for
    /// <c>ProtectionRules.IsAutoProtected</c> does not spend one of these; this
    /// count is only for the GM's own picks.
    /// </summary>
    public int? ProtectionSlots { get; set; }

    /// <summary>
    /// How many of the off-season draft's opening rounds are steal rounds, in
    /// which a GM takes an exposed player off a rival's roster. Les Mordus: 2.
    /// Null or 0 = the draft goes straight to its rookie / free-agent rounds.
    ///
    /// Deliberately independent of <see cref="DraftRounds"/>, and it is not an
    /// error for it to differ: they size two different drafts that happen to run
    /// back to back. Les Mordus runs 2 steal rounds and 3 rookie rounds.
    ///
    /// Unlike <see cref="DraftRounds"/> this generates no rows — a steal turn is
    /// not tradable, so every team gets exactly this many and there is nothing
    /// to own (Nick, 2026-08-25).
    /// </summary>
    public int? StealRounds { get; set; }

    /// <summary>
    /// The most players one team may lose across a whole steal draft. Les
    /// Mordus: 2. Null = uncapped, which is a real choice and not a limit of
    /// zero.
    ///
    /// It closes the pool from underneath as the draft runs: once a team reaches
    /// this, every remaining player it holds stops being available to everyone.
    /// That is why the available list is recomputed per turn and never cached.
    /// </summary>
    public int? MaxLossesPerTeam { get; set; }

    /// <summary>Active forward slots per week. Les Mordus: 9.</summary>
    public int ActiveForwards { get; set; }

    /// <summary>Active defence slots per week. Les Mordus: 4.</summary>
    public int ActiveDefense { get; set; }

    /// <summary>Active goalie slots per week. Les Mordus: 1.</summary>
    public int ActiveGoalies { get; set; }

    public DateTime CreatedUtc { get; set; }

    public User? Commissioner { get; set; }

    public ICollection<LeagueMember> Members { get; set; } = [];

    public ICollection<LeagueScoringRule> ScoringRules { get; set; } = [];

    public ICollection<Team> Teams { get; set; } = [];

    public ICollection<RosterSpot> RosterSpots { get; set; } = [];

    public ICollection<Trade> Trades { get; set; } = [];

    public ICollection<DraftPick> DraftPicks { get; set; } = [];

    /// <summary>
    /// This league's own playthrough history — see <see cref="LeagueSeason"/>.
    /// Exactly one row here is ever not <c>Complete</c>.
    /// </summary>
    public ICollection<LeagueSeason> Seasons { get; set; } = [];
}
