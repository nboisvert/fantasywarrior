using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// A pooler's team inside a league. Stored at leagues/{leagueId}/teams/{username}:
/// the document id is the owner's normalized username (one team per user per league).
/// </summary>
[FirestoreData]
public sealed class Team
{
    [FirestoreProperty("name")]
    public string Name { get; set; } = "";

    [FirestoreProperty("ownerUsername")]
    public string OwnerUsername { get; set; } = "";

    /// <summary>NHL player ids on the roster.</summary>
    [FirestoreProperty("playerIds")]
    public List<long> PlayerIds { get; set; } = [];

    [FirestoreProperty("createdUtc")]
    public Timestamp CreatedUtc { get; set; }
}
