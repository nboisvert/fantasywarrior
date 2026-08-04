using FantasyWarrior.Data.Entities;

namespace FantasyWarrior.Jobs.News;

/// <summary>
/// Turns a source's short injury label into the one distinction the app acts
/// on: is this player hurt, or is he banned from playing?
///
/// Both keep him out of the lineup, which is why they share the same
/// unavailability marker on the Team grid, and both are listed on the same
/// pages — FantasySP files "Suspension" in its Injury column, next to "Hip"
/// and "Achilles". But telling a GM that Charlie McAvoy is *injured* when he
/// was suspended six games for slashing is simply wrong, so the icon differs
/// and the kind is decided once, here, at the moment the text is read.
///
/// Anything that is not recognisably a suspension is an injury: the labels are
/// free text from two sites that can invent a new body part any day, and
/// "hurt, cause unclear" is the safer wrong answer of the two.
/// </summary>
public static class InjuryClassifier
{
    private static readonly string[] SuspensionWords = ["suspend", "suspension", "banned"];

    public static string StatusFor(string? injuryType)
    {
        var text = (injuryType ?? "").Trim();
        return SuspensionWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase))
            ? InjuryStatuses.Suspended
            : InjuryStatuses.Injured;
    }
}
