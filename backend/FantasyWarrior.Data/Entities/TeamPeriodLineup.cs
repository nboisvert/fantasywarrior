namespace FantasyWarrior.Data.Entities;

public static class LineupSetBy
{
    /// <summary>
    /// Carried forward from the previous week and topped up, because the GM did
    /// not submit one. The UI surfaces this so a GM can tell his own choices
    /// from the app's.
    /// </summary>
    public const string Auto = "auto";
}

/// <summary>
/// Who set a team's lineup for a week, and when.
///
/// Small on purpose: the *contents* of the lineup live as
/// <see cref="RosterAssignment.IsActive"/> flags on the spot-week rows, where
/// they can be joined and aggregated. This row carries only the provenance,
/// which has nowhere else to live and which the UI actually shows ("we picked
/// this for you").
/// </summary>
public sealed class TeamPeriodLineup
{
    public int TeamId { get; set; }

    public int PeriodId { get; set; }

    /// <summary>The username that set it, or <see cref="LineupSetBy.Auto"/>.</summary>
    public required string SetBy { get; set; }

    public DateTime SubmittedUtc { get; set; }

    public Team? Team { get; set; }

    public Period? Period { get; set; }
}
