namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One team's selection in one round of one draft year — an asset in its own
/// right, tradable before it is used.
///
/// <see cref="OriginalTeamId"/> and <see cref="CurrentTeamId"/> are separate so
/// the pick's provenance survives every trade: "Pittsburgh's 2nd, via Boston"
/// is those two columns and nothing else. Collapsing them into one owner column
/// would lose the only thing that makes a traded pick interesting to talk
/// about.
///
/// When the pick is used, <see cref="PlayerId"/> is set and a
/// <see cref="RosterSpot"/> opens pointing back here via
/// <c>StartDraftPickId</c> — so a player's roster history says "drafted with
/// this pick", not just "drafted".
///
/// Modelled now, exercised later: no draft mechanism or UI exists yet. It is
/// here so building one needs no migration.
/// </summary>
public sealed class DraftPick
{
    public int DraftPickId { get; set; }

    public int LeagueId { get; set; }

    /// <summary>Calendar year of the draft, e.g. 2027.</summary>
    public int Year { get; set; }

    public int Round { get; set; }

    /// <summary>Position within the round, once the order is known. Null until then.</summary>
    public int? PickInRound { get; set; }

    /// <summary>Whose pick this originally was. Never changes.</summary>
    public int OriginalTeamId { get; set; }

    /// <summary>Who holds it now. Changes on every trade.</summary>
    public int CurrentTeamId { get; set; }

    /// <summary>The player taken with it; null while the pick is unused.</summary>
    public long? PlayerId { get; set; }

    public DateTime? UsedUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    public League? League { get; set; }

    public Team? OriginalTeam { get; set; }

    public Team? CurrentTeam { get; set; }

    public Player? Player { get; set; }
}
