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
}
