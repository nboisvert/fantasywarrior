namespace FantasyWarrior.Data.Entities;

public static class PlayerStatus
{
    /// <summary>On an NHL team's season roster.</summary>
    public const string Nhl = "nhl";

    public const string Prospect = "prospect";
}

/// <summary>
/// A player or prospect in the NHL ecosystem. The primary key is the NHL's own
/// player id, not an identity column: it is stable, globally unique, and every
/// external payload we ingest is already keyed by it.
///
/// Salary lives in <see cref="PlayerContract"/> rather than in a column here.
/// Under Firestore it was a <c>capHit</c> field that needed hand-written
/// merge-field protection so the nightly roster sync would not wipe it; a
/// separate table makes that structural, and it models the thing correctly —
/// a cap hit belongs to a contract year, not to a player forever.
/// </summary>
public sealed class Player
{
    /// <summary>NHL player id.</summary>
    public long PlayerId { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    /// <summary>Raw NHL position code: C, L, R, D or G.</summary>
    public required string Position { get; set; }

    /// <summary>
    /// F, D or G — a computed, stored column derived from <see cref="Position"/>
    /// by the database, mirroring <c>PositionGroups.From</c>.
    ///
    /// Computed rather than written by the app so it can never drift, and stored
    /// rather than virtual so it can be indexed and filtered cheaply. Note this
    /// is the *player's current* group; a roster spot deliberately freezes its
    /// own copy at open (see <see cref="RosterSpot.PositionGroup"/>).
    /// </summary>
    public string PositionGroup { get; private set; } = "";

    public string? TeamAbbrev { get; set; }

    /// <summary>"nhl" or "prospect" — see <see cref="PlayerStatus"/>.</summary>
    public required string Status { get; set; }

    public int? SweaterNumber { get; set; }

    public string? ShootsCatches { get; set; }

    public DateOnly? BirthDate { get; set; }

    public string? BirthCountry { get; set; }

    public int? HeightCm { get; set; }

    public int? WeightKg { get; set; }

    public string? HeadshotUrl { get; set; }

    // --- NHL entry draft. All null means undrafted, which is why DraftChecked
    // exists separately: "we looked and he was undrafted" and "we never looked"
    // are different states, and only the second one should be re-queried.

    public int? DraftYear { get; set; }

    public int? DraftRound { get; set; }

    public int? DraftOverall { get; set; }

    public string? DraftTeamAbbrev { get; set; }

    public bool DraftChecked { get; set; }

    /// <summary>
    /// This player's slug on CapWages, remembered once the contract import has
    /// matched him. Turns every later run into an exact lookup instead of a
    /// name match, and makes a rename on either side harmless.
    /// </summary>
    public string? CapWagesSlug { get; set; }

    /// <summary>
    /// Last time career-sync fetched this player's <see cref="PlayerCareerSeasonStat"/>
    /// rows. Null means never. Unlike <see cref="DraftChecked"/> this is a
    /// timestamp, not a one-time flag: the current season's row keeps
    /// changing all year, so career-sync re-fetches on a rolling staleness
    /// window rather than once forever.
    /// </summary>
    public DateTime? CareerStatsSyncedUtc { get; set; }

    /// <summary>
    /// Regular-season NHL games this player has played in his whole career —
    /// the sum of his <see cref="PlayerCareerSeasonStat"/> rows where the league
    /// is the NHL. Null exactly when <see cref="CareerStatsSyncedUtc"/> is:
    /// **zero games and "never looked" are different states**, and a veteran
    /// whose sync failed must not read as a rookie.
    ///
    /// Stored rather than summed on demand, for the same reason
    /// <see cref="PositionGroup"/> is: it has one writer — career-sync, which
    /// sets it in the same save as the rows it derives from, so it cannot drift
    /// — and a plain column can be compared in SQL, where a sum over another
    /// table would force the threshold that reads it to be written twice.
    ///
    /// It is what decides <c>ProtectionRules.IsAutoProtected</c>: too few NHL
    /// games and nobody can draft this player away from his GM. The measurement
    /// lives here; the verdict stays derived, one comparison away, so moving a
    /// threshold never means rewriting rows.
    ///
    /// Stale by at most career-sync's freshness window (30 days) because the
    /// current season's row keeps changing all year. Irrelevant at thresholds of
    /// 50 and 100 games; the draft itself will freeze the figure rather than
    /// read it live.
    /// </summary>
    public int? CareerNhlGames { get; set; }

    public DateTime LastSyncedUtc { get; set; }

    public NhlTeam? Team { get; set; }

    public ICollection<PlayerContract> Contracts { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}";
}
