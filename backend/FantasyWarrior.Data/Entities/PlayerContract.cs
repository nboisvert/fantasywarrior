namespace FantasyWarrior.Data.Entities;

public static class ContractSource
{
    public const string CapWages = "capwages";
    public const string Csv = "csv";

    /// <summary>
    /// Placeholder figures scaled across the top scorers, never real contract
    /// data. Tagged so a cap total computed from estimates can never be
    /// mistaken for a real one.
    /// </summary>
    public const string Estimated = "estimated";
}

/// <summary>
/// One player's contract terms for one season.
///
/// A row per season rather than a single <c>capHit</c> column: cap hits change
/// every year, and a pool that runs across seasons needs to know what a player
/// cost *then*, not only what he costs now.
/// </summary>
public sealed class PlayerContract
{
    public int PlayerContractId { get; set; }

    public long PlayerId { get; set; }

    /// <summary>NHL season, e.g. "20252026".</summary>
    public required string Season { get; set; }

    /// <summary>Annual cap hit in whole dollars.</summary>
    public long CapHit { get; set; }

    /// <summary>Average annual value, when the source distinguishes it from the cap hit.</summary>
    public long? Aav { get; set; }

    public long? TotalValue { get; set; }

    public int? YearsRemaining { get; set; }

    /// <summary>NMC, NTC, M-NTC… free text, whatever the source reports.</summary>
    public string? ClauseType { get; set; }

    /// <summary>See <see cref="ContractSource"/>.</summary>
    public required string Source { get; set; }

    public DateTime ImportedUtc { get; set; }

    public Player? Player { get; set; }
}
