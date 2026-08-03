namespace FantasyWarrior.Data.Entities.Views;

/// <summary>
/// A player's regular-season totals, aggregated from the game log.
///
/// A view, not a table. Under Firestore this was a physical
/// <c>playerSeasonStats</c> collection with a <c>throughDate</c> field and its
/// own cache-invalidation rules, all of which existed to avoid re-reading 51,000
/// documents against a 50,000-read daily quota. Here it is a GROUP BY over an
/// indexed column and the entire apparatus is unnecessary.
///
/// Note this view is deliberately unbounded — whole season, whatever has been
/// synced. Totals *as of a given day* (what a season replay needs) are a
/// parameterised query rather than a second view, because a view cannot take
/// the date as an argument.
/// </summary>
public sealed class PlayerSeasonStatsView
{
    public long PlayerId { get; set; }
    public string Season { get; set; } = "";
    public int GamesPlayed { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int PlusMinus { get; set; }
    public int Pim { get; set; }
    public int Shots { get; set; }
    public int Hits { get; set; }
    public int BlockedShots { get; set; }
    public int Wins { get; set; }
    public int OtLosses { get; set; }
    public int Shutouts { get; set; }
    public int GoalsAgainst { get; set; }
    public int Saves { get; set; }
    public int ShotsAgainst { get; set; }

    public int Points => Goals + Assists;
}

/// <summary>
/// What one roster spot has been worth to its team: the sum of its weekly
/// assignments, split by whether the GM had him active.
///
/// The bench half is what powers "you left 14 points on the bench", which is
/// the single most-read number on the Team screen.
/// </summary>
public sealed class RosterSpotTotalsView
{
    public int RosterSpotId { get; set; }
    public int LeagueId { get; set; }
    public int TeamId { get; set; }
    public long PlayerId { get; set; }

    /// <summary>Points earned while in the active lineup — the only ones that count.</summary>
    public double ActivePoints { get; set; }

    public double BenchPoints { get; set; }

    /// <summary>Games played while active — the honest denominator for points per game.</summary>
    public int ActiveGamesPlayed { get; set; }

    public int ActiveGoals { get; set; }
    public int ActiveAssists { get; set; }

    /// <summary>Banked: from weeks that are already finalized and can never move.</summary>
    public double FinalizedActivePoints { get; set; }
}

/// <summary>One team's result for one scoring week — the weekly history chart.</summary>
public sealed class TeamPeriodScoreView
{
    public int TeamId { get; set; }
    public int PeriodId { get; set; }
    public int PeriodNumber { get; set; }
    public string Season { get; set; } = "";
    public double ActivePoints { get; set; }
    public double BenchPoints { get; set; }
    public bool IsFinalized { get; set; }
}

/// <summary>
/// The standings row for one team.
///
/// <c>Score = FinalizedPoints + LivePoints</c> holds by construction here,
/// because both sides are computed from the same rows in the same statement.
/// On Firestore this was three stored fields kept in agreement by hand, and
/// keeping them in agreement is where its scoring bugs came from.
/// </summary>
public sealed class StandingsView
{
    public int TeamId { get; set; }
    public int LeagueId { get; set; }

    /// <summary>Everything earned while active, banked or not — what the standings show.</summary>
    public double Score { get; set; }

    /// <summary>The part that is banked and can never move again.</summary>
    public double FinalizedScore { get; set; }

    /// <summary>The unbanked remainder: in practice, the week in progress.</summary>
    public double LivePoints { get; set; }

    /// <summary>What this team's benched players have produced, all season.</summary>
    public double BenchScore { get; set; }

    public int RosterGamesPlayed { get; set; }

    /// <summary>Players currently held — open roster spots only.</summary>
    public int PlayerCount { get; set; }

    /// <summary>Summed cap hits of the current roster; 0 for players with no contract on file.</summary>
    public long CapTotal { get; set; }
}

/// <summary>
/// What a team's <b>accepted but not yet executed</b> trades already commit it
/// to — a delta, not a total.
///
/// This exists because <see cref="StandingsView"/> is the wrong number to
/// validate a trade against. Standings show the roster as it is today; an
/// accepted trade is a decision nobody can take back, and it lands at the next
/// week boundary. Validating a new offer against today's cap lets a GM accept a
/// $9M contract in the morning and bust the cap in the afternoon, with both
/// trades individually looking fine.
///
/// Kept separate from the standings rather than folded into them on purpose:
/// the two answer different questions, and the league table must keep showing
/// what is true now, not what has been promised.
///
/// No row for a team with nothing pending — that is a zero delta, and the
/// caller treats an absent row as such.
/// </summary>
public sealed class TeamCommitmentView
{
    public int TeamId { get; set; }
    public int LeagueId { get; set; }

    /// <summary>
    /// Cap arriving minus cap leaving. Players with no contract on file count
    /// as 0, exactly as they do in the standings — the two must agree or the
    /// arithmetic between them is meaningless.
    /// </summary>
    public long CapDelta { get; set; }

    /// <summary>Players arriving minus players leaving. Draft picks are not roster spots.</summary>
    public int CountDelta { get; set; }
}

/// <summary>
/// One Pooler's (team's) whole trade-voting history, from either side of the
/// table — a team's own trades as proposer and as counterparty both count
/// toward the same row. No row at all for a team with no processed trades:
/// there's nothing to report, not a zeroed one.
/// </summary>
public sealed class PoolerTradeRecordView
{
    public int TeamId { get; set; }
    public int LeagueId { get; set; }

    public int TradesTotal { get; set; }

    /// <summary>Trades where this team's side got the plurality of votes.</summary>
    public int TradesWon { get; set; }

    public int TradesLost { get; set; }

    public int TradesFair { get; set; }

    /// <summary>Raw vote counts across every trade, not just the decided ones.</summary>
    public int VotesFor { get; set; }

    public int VotesAgainst { get; set; }

    public int VotesFair { get; set; }

    /// <summary>
    /// 0-100, centered at 50. Only decided trades (<see cref="TradesWon"/>/
    /// <see cref="TradesLost"/>) move it — fair trades and exact ties are
    /// neutral, not zero. Null until this team has at least one decided
    /// trade, so "no data" and "dead even" never look the same.
    /// </summary>
    public double? TraderRating { get; set; }
}

/// <summary>
/// One user's cockcoin balance — SUM over <see cref="CockcoinAward"/>, same
/// "derive it from the ledger" story as every other total in this schema. No
/// row at all for a user who has never earned any; the API is what turns
/// that into a displayed 0, same as a brand new Pooler's trade record.
/// </summary>
public sealed class CockcoinBalanceView
{
    public int UserId { get; set; }
    public int Balance { get; set; }
}
