namespace FantasyWarrior.Data.Entities;

/// <summary>
/// The season-replay cursor. A single row, enforced by a CHECK on the key.
///
/// One writer (the test-mode jobs), one row, and every reader — the nightly
/// pipeline, the local API, the deployed API — takes its idea of "today" from
/// it. There is nothing to keep in sync and nothing to parse.
/// </summary>
public sealed class SimulationState
{
    /// <summary>Always 1; a CHECK constraint makes a second row impossible.</summary>
    public byte SimulationStateId { get; set; } = 1;

    /// <summary>
    /// The last game day whose results are known.
    ///
    /// The simulated instant derives as the *next* day at noon UTC, which makes
    /// <c>PoolClock.TodayEt</c> = AsOfDate + 1 and <c>PoolClock.LastStatDate</c>
    /// = AsOfDate — exactly the real-world relationship, which is why no scoring
    /// code needs a special case for simulation.
    /// </summary>
    public DateOnly AsOfDate { get; set; }

    public required string Season { get; set; }

    /// <summary>Lets the replay be paused without losing the cursor.</summary>
    public bool Enabled { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
