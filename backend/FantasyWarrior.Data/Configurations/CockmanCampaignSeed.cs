using FantasyWarrior.Data.Entities;

namespace FantasyWarrior.Data.Configurations;

/// <summary>
/// Seeded campaigns — no admin screen exists yet, so a new campaign is a
/// migration, the same way <see cref="NhlTeamSeed"/> is. See
/// <c>.claude/doc/cockman-concept.md</c>.
/// </summary>
public static class CockmanCampaignSeed
{
    public static IReadOnlyList<CockmanCampaign> All { get; } =
    [
        new CockmanCampaign
        {
            CockmanCampaignId = 1,
            Key = "welcome",
            HasQuestion = false,
            ChoiceKeys = null,
            RewardAmount = null,
            StartUtc = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            EndUtc = null,
            CreatedUtc = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
        },
    ];
}
