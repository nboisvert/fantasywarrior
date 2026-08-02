namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One season/league/team stint of a player's career, sourced from the NHL's
/// own player-landing endpoint — not just the NHL seasons this app has
/// tracked since going live, but junior/NCAA/European history too.
///
/// Distinct from <see cref="PlayerGameStat"/> (this app's own per-game NHL
/// data, the source of truth for scoring) and from the
/// <c>PlayerSeasonStatsView</c> (this app's in-pool season aggregate) — this
/// table is purely reference data for the Player Card's Career tab.
///
/// A surrogate key, not a natural one: a mid-season trade gives two rows for
/// the same season/league/game-type with different teams.
/// </summary>
public sealed class PlayerCareerSeasonStat
{
    public long PlayerCareerSeasonStatId { get; set; }

    public long PlayerId { get; set; }

    /// <summary>8-char, e.g. "20122013" — same convention as Game/PlayerGameStat.</summary>
    public required string Season { get; set; }

    /// <summary>See <see cref="GameType"/>. Regular season only for now.</summary>
    public byte GameType { get; set; }

    public required string LeagueAbbrev { get; set; }

    public string? TeamName { get; set; }

    public int GamesPlayed { get; set; }

    // --- skaters ---

    public int? Goals { get; set; }

    public int? Assists { get; set; }

    public int? Points { get; set; }

    public int? Pim { get; set; }

    public int? PlusMinus { get; set; }

    // --- goalies ---

    public int? Wins { get; set; }

    public int? Losses { get; set; }

    public int? OtLosses { get; set; }

    public int? GoalsAgainst { get; set; }

    /// <summary>Precomputed by the NHL API — not recomputed here.</summary>
    public double? GoalsAgainstAvg { get; set; }

    /// <summary>Precomputed by the NHL API — not recomputed here.</summary>
    public double? SavePctg { get; set; }

    public int? Shutouts { get; set; }

    public Player? Player { get; set; }
}
