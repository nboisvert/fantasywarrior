namespace FantasyWarrior.Data.Entities;

/// <summary>
/// The two ways a player can be unavailable, as far as this app can tell.
///
/// Neither source publishes a severity ("Out", "IR", "Day-to-day") — both give
/// a short cause and nothing else — so <see cref="PlayerInjury.Status"/> carries
/// the *kind* of unavailability instead, which is the distinction the UI
/// actually draws. See <c>InjuryClassifier</c>, which is the only writer.
/// </summary>
public static class InjuryStatuses
{
    public const string Injured = "Injured";
    public const string Suspended = "Suspended";
}

/// <summary>
/// A player's current injury status, as reported by a news source.
///
/// One open row (<see cref="ResolvedUtc"/> null) per player per source while
/// the source lists him. The sources are *state* pages, not event streams:
/// they publish who is hurt today, so a player disappearing from one is how
/// that source says "cleared" — which is why news-sync closes the row rather
/// than waiting for an announcement that never comes.
///
/// This is the durable half of the injury story. The matching NewsItem is
/// deleted once a source stops listing the player (it is no longer true), but
/// the row here keeps ReportedUtc/ResolvedUtc permanently.
/// </summary>
public sealed class PlayerInjury
{
    public int PlayerInjuryId { get; set; }

    public long PlayerId { get; set; }

    /// <summary>See <see cref="InjuryStatuses"/> — injured, or suspended.</summary>
    public required string Status { get; set; }

    /// <summary>Upper body, knee, illness… free text from the source.</summary>
    public string? InjuryType { get; set; }

    /// <summary>
    /// Never populated: neither source states a return date in a field, only
    /// in prose ("out until at least early November"), and guessing a date out
    /// of a sentence would be worse than showing none.
    /// </summary>
    public DateOnly? ExpectedReturn { get; set; }

    /// <summary>See <see cref="NewsSourceName"/>.</summary>
    public required string Source { get; set; }

    public DateTime ReportedUtc { get; set; }

    /// <summary>Null while the injury is current; set when the player is cleared.</summary>
    public DateTime? ResolvedUtc { get; set; }

    public Player? Player { get; set; }
}
