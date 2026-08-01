namespace FantasyWarrior.Data.Entities;

public enum TradeStatus : byte
{
    Pending = 0,

    /// <summary>The counterparty rejected the offer.</summary>
    Declined = 1,

    /// <summary>
    /// The proposer withdrew his own still-pending offer. Distinct from
    /// <see cref="Declined"/> so the status alone says who acted.
    /// </summary>
    Cancelled = 2,

    /// <summary>Agreed, but not yet executed — rosters are untouched until the period rolls over.</summary>
    Accepted = 3,

    Processed = 4,
}

/// <summary>
/// A swap of assets between two teams.
///
/// **Accepting does not execute it.** An accepted trade waits for the nightly
/// job to run it at a period boundary, so a player never has two owners for one
/// scoring week — the assumption the whole banked-points model rests on.
/// Execution closes the outgoing roster spots and opens the incoming ones, both
/// referencing this trade.
/// </summary>
public sealed class Trade
{
    public int TradeId { get; set; }

    public int LeagueId { get; set; }

    public int ProposerTeamId { get; set; }

    public int CounterpartyTeamId { get; set; }

    public TradeStatus Status { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? RespondedUtc { get; set; }

    public DateTime? ProcessedUtc { get; set; }

    /// <summary>
    /// The day the swap took effect — always a period start, never "today".
    /// Null until processed.
    /// </summary>
    public DateOnly? EffectiveDate { get; set; }

    public League? League { get; set; }

    public Team? ProposerTeam { get; set; }

    public Team? CounterpartyTeam { get; set; }

    public ICollection<TradeAsset> Assets { get; set; } = [];

    public ICollection<TradeVote> Votes { get; set; } = [];
}
