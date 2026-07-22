using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// A pool/league. Multi-tenancy root: everything league-scoped lives in
/// subcollections (teams, later: standings, transactions).
/// The document id doubles as the invite code for now.
/// </summary>
[FirestoreData]
public sealed class League
{
    [FirestoreProperty("name")]
    public string Name { get; set; } = "";

    /// <summary>NHL season the league runs on, e.g. "20262027".</summary>
    [FirestoreProperty("season")]
    public string Season { get; set; } = "";

    [FirestoreProperty("commissionerUsername")]
    public string CommissionerUsername { get; set; } = "";

    /// <summary>Normalized usernames; array-contains queries drive "my leagues".</summary>
    [FirestoreProperty("memberUsernames")]
    public List<string> MemberUsernames { get; set; } = [];

    /// <summary>Salary cap per team in USD; null = no cap rule.</summary>
    [FirestoreProperty("capAmount")]
    public long? CapAmount { get; set; }

    [FirestoreProperty("createdUtc")]
    public Timestamp CreatedUtc { get; set; }
}
