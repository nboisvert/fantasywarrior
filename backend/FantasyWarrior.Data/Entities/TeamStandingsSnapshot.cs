namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One team's rank and last-night points, as of one game night — written
/// once per night by <c>StandingsSnapshotJob</c>, read back by the
/// Standings screen's rank-movement pill.
///
/// <b>Rank movement always compares the two most recent rows for a team</b>
/// (this one vs the one before it), never "live standings right now vs one
/// snapshot." Between two nightly runs, live standings already equal the
/// most recent snapshot's order — nothing else moves them intraday — so a
/// live-vs-snapshot comparison would read "no movement" almost always,
/// which is not what "movement since last night" means. Comparing two rows
/// needs no reasoning about what time it is right now.
/// </summary>
public sealed class TeamStandingsSnapshot
{
    public long TeamStandingsSnapshotId { get; set; }
    public int TeamId { get; set; }

    /// <summary>The game night this row represents — PoolClock.LastStatDate
    /// at the moment the nightly job ran.</summary>
    public DateOnly AsOfDate { get; set; }

    /// <summary>1-based position in that night's standings, by Score descending.</summary>
    public int Rank { get; set; }

    /// <summary>Fantasy points this team's active roster earned specifically
    /// on AsOfDate's games.</summary>
    public double LastNightPoints { get; set; }

    public DateTime CreatedUtc { get; set; }

    public Team? Team { get; set; }
}
