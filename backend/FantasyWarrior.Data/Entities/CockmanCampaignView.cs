namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One user's encounter with one <see cref="CockmanCampaign"/> — its mere
/// existence means "seen", which is the entire mechanism that stops a
/// campaign from reappearing once dismissed or answered.
/// </summary>
public sealed class CockmanCampaignView
{
    public int CockmanCampaignId { get; set; }
    public int UserId { get; set; }
    public DateTime ViewedUtc { get; set; }

    /// <summary>One of the campaign's ChoiceKeys; null if dismissed without answering.</summary>
    public string? ChosenAnswer { get; set; }

    /// <summary>Set iff ChosenAnswer is set and the reward was actually credited.</summary>
    public DateTime? RewardedUtc { get; set; }

    public CockmanCampaign? Campaign { get; set; }
    public User? User { get; set; }
}
