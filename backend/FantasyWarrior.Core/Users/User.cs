using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Users;

/// <summary>
/// Document id = normalized username (lowercase, trimmed).
/// TEMPORARY auth model: username-only login, no password/Firebase Auth yet.
/// When Firebase Auth lands (Phase 3 completion), this doc gains the auth uid.
/// </summary>
[FirestoreData]
public sealed class User
{
    [FirestoreProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [FirestoreProperty("createdUtc")]
    public Timestamp CreatedUtc { get; set; }

    [FirestoreProperty("lastLoginUtc")]
    public Timestamp LastLoginUtc { get; set; }
}
