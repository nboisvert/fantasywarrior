namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One player's line in one game — the GameLog, and the source of truth for
/// every number in the app. Roughly 51,000 rows per season.
///
/// Skater columns are null for goalies and vice versa. They are typed columns
/// rather than a key/value bag on purpose: querying them is the entire reason
/// for being on SQL. The flexible, league-configurable *scoring* representation
/// stays a map (<c>StatLine</c>/<c>StatKeys</c>), and
/// <c>StatColumns.ToStatLine</c> is the one adapter between the two.
///
/// <see cref="GameDate"/>, <see cref="Season"/> and <see cref="GameType"/> are
/// denormalised from <see cref="Game"/> so the hot query — every line in a date
/// range — is served by one covering index with no join.
/// </summary>
public sealed class PlayerGameStat
{
    public long GameId { get; set; }

    public long PlayerId { get; set; }

    public DateOnly GameDate { get; set; }

    public required string Season { get; set; }

    public byte GameType { get; set; }

    public required string TeamAbbrev { get; set; }

    public required string OpponentAbbrev { get; set; }

    /// <summary>Raw NHL position code as it was on the night.</summary>
    public required string Position { get; set; }

    public bool IsGoalie { get; set; }

    public bool IsHome { get; set; }

    /// <summary>Time on ice as "MM:SS", the NHL's own format.</summary>
    public string? Toi { get; set; }

    public int Pim { get; set; }

    // --- skaters ---

    public int? Goals { get; set; }

    public int? Assists { get; set; }

    public int? Points { get; set; }

    public int? PlusMinus { get; set; }

    public int? Shots { get; set; }

    public int? Hits { get; set; }

    public int? BlockedShots { get; set; }

    public int? PowerPlayGoals { get; set; }

    // --- goalies ---

    public int? ShotsAgainst { get; set; }

    public int? Saves { get; set; }

    public int? GoalsAgainst { get; set; }

    /// <summary>W, L or O (overtime/shootout loss); null when no decision.</summary>
    public string? Decision { get; set; }

    public bool? Starter { get; set; }

    /// <summary>Solo full game with zero goals against.</summary>
    public bool? Shutout { get; set; }

    /// <summary>Charged with an OT/SO loss (decision "O").</summary>
    public bool? OtLoss { get; set; }

    public DateTime SyncedUtc { get; set; }

    public Game? Game { get; set; }

    public Player? Player { get; set; }
}
