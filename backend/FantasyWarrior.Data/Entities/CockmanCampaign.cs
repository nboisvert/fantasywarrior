namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One scheduled Cockman notification — a message, with an optional
/// multiple-choice question and cockcoin reward, shown to a user once while
/// its window is open. The row is structure and scheduling only; the actual
/// bilingual copy lives in the frontend's <c>cockmanCampaigns</c> dictionary,
/// keyed by <see cref="Key"/> — the same convention as every other Cockman
/// line (see <c>CockmanChat.tsx</c>'s scripted dialogue).
///
/// See <c>.claude/doc/cockman-concept.md</c>.
/// </summary>
public sealed class CockmanCampaign
{
    public int CockmanCampaignId { get; set; }

    /// <summary>Stable i18n lookup key, e.g. "welcome".</summary>
    public required string Key { get; set; }

    public bool HasQuestion { get; set; }

    /// <summary>Valid answer keys, in display order. Null for a message-only
    /// campaign.</summary>
    public IReadOnlyList<string>? ChoiceKeys { get; set; }

    /// <summary>Cockcoin awarded for answering. Only meaningful when
    /// <see cref="HasQuestion"/> is true.</summary>
    public int? RewardAmount { get; set; }

    public DateTime StartUtc { get; set; }

    /// <summary>Null = runs forever (the welcome campaign).</summary>
    public DateTime? EndUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    public ICollection<CockmanCampaignView> Views { get; set; } = [];
}
