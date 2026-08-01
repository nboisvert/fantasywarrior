namespace FantasyWarrior.Data.Entities;

/// <summary>
/// A player's current injury status, as reported by a news source.
///
/// Modelled now, populated later: the news scrapers already parse team and
/// injury type out of the injuries pages and currently throw both away into a
/// headline string. Giving that a home means the roster and lineup screens can
/// eventually warn "this player is out" without re-parsing prose.
/// </summary>
public sealed class PlayerInjury
{
    public int PlayerInjuryId { get; set; }

    public long PlayerId { get; set; }

    /// <summary>Out, IR, LTIR, Day-to-day, Questionable… as the source words it.</summary>
    public required string Status { get; set; }

    /// <summary>Upper body, knee, illness… free text from the source.</summary>
    public string? InjuryType { get; set; }

    public DateOnly? ExpectedReturn { get; set; }

    /// <summary>See <see cref="NewsSourceName"/>.</summary>
    public required string Source { get; set; }

    public DateTime ReportedUtc { get; set; }

    /// <summary>Null while the injury is current; set when the player is cleared.</summary>
    public DateTime? ResolvedUtc { get; set; }

    public Player? Player { get; set; }
}
