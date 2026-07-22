using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Players;

/// <summary>
/// A player or prospect in the NHL ecosystem. Document id = NHL player id.
/// Salary/contract data (capHit) comes from a separate source than the NHL API
/// and stays null until imported.
/// </summary>
[FirestoreData]
public sealed class Player
{
    [FirestoreProperty("nhlId")]
    public long NhlId { get; set; }

    [FirestoreProperty("firstName")]
    public string FirstName { get; set; } = "";

    [FirestoreProperty("lastName")]
    public string LastName { get; set; } = "";

    /// <summary>C, L, R, D or G.</summary>
    [FirestoreProperty("position")]
    public string Position { get; set; } = "";

    [FirestoreProperty("teamAbbrev")]
    public string TeamAbbrev { get; set; } = "";

    /// <summary>"nhl" (on a team season roster) or "prospect".</summary>
    [FirestoreProperty("status")]
    public string Status { get; set; } = "";

    [FirestoreProperty("sweaterNumber")]
    public int? SweaterNumber { get; set; }

    [FirestoreProperty("shootsCatches")]
    public string? ShootsCatches { get; set; }

    [FirestoreProperty("birthDate")]
    public string? BirthDate { get; set; }

    [FirestoreProperty("birthCountry")]
    public string? BirthCountry { get; set; }

    [FirestoreProperty("heightCm")]
    public int? HeightCm { get; set; }

    [FirestoreProperty("weightKg")]
    public int? WeightKg { get; set; }

    [FirestoreProperty("headshotUrl")]
    public string? HeadshotUrl { get; set; }

    /// <summary>Annual cap hit in USD. Null until salary data is imported (PuckPedia/CSV).</summary>
    [FirestoreProperty("capHit")]
    public long? CapHit { get; set; }

    [FirestoreProperty("lastSyncedUtc")]
    public Timestamp LastSyncedUtc { get; set; }
}
