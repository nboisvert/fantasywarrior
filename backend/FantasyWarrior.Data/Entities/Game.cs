namespace FantasyWarrior.Data.Entities;

public static class GameType
{
    /// <summary>Regular season. The only type that scores — see scoring-model.md §4.</summary>
    public const byte RegularSeason = 2;

    public const byte Playoffs = 3;
}

/// <summary>
/// One NHL game, keyed by the NHL's own game id.
///
/// <see cref="GameDate"/> is the NHL's Eastern game date, written verbatim from
/// the API. A West Coast game starting Sunday 22:30 ET and ending Monday 01:15
/// ET carries the Sunday date and therefore belongs to Sunday's scoring week —
/// this is the anchor for the whole calendar (see <c>PoolClock</c>).
/// </summary>
public sealed class Game
{
    /// <summary>NHL game id.</summary>
    public long GameId { get; set; }

    /// <summary>e.g. "20252026".</summary>
    public required string Season { get; set; }

    /// <summary>See <see cref="GameType"/>.</summary>
    public byte GameType { get; set; }

    public DateOnly GameDate { get; set; }

    public required string HomeTeamAbbrev { get; set; }

    public required string AwayTeamAbbrev { get; set; }

    public int HomeScore { get; set; }

    public int AwayScore { get; set; }

    /// <summary>REG, OT or SO.</summary>
    public string? LastPeriodType { get; set; }

    public DateTime SyncedUtc { get; set; }

    public ICollection<PlayerGameStat> PlayerLines { get; set; } = [];
}
