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

    public DateTime LastSyncedUtc { get; set; }

    public NhlTeam? Team { get; set; }

    public ICollection<PlayerContract> Contracts { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}";
}
