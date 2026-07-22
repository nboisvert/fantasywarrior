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

    // --- computed by the scoring job / roster endpoints ---

    /// <summary>Displayed score: rawTopXScore + adjustmentsTotal.</summary>
    [FirestoreProperty("score")]
    public double Score { get; set; }

    /// <summary>Sum of the top-X counted players' season fantasy points.</summary>
    [FirestoreProperty("rawTopXScore")]
    public double RawTopXScore { get; set; }

    /// <summary>Sum of the transaction adjustment ledger (subcollection `adjustments`).</summary>
    [FirestoreProperty("adjustmentsTotal")]
    public double AdjustmentsTotal { get; set; }

    /// <summary>Players currently counting toward the score ("compteDansLesPoints").</summary>
    [FirestoreProperty("countedPlayerIds")]
    public List<long> CountedPlayerIds { get; set; } = [];

    /// <summary>Season fantasy points per rostered player (map keys are playerIds as strings).</summary>
    [FirestoreProperty("playerPoints")]
    public Dictionary<string, double> PlayerPoints { get; set; } = [];

    [FirestoreProperty("scoreUpdatedUtc")]
    public Timestamp? ScoreUpdatedUtc { get; set; }
}
