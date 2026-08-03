namespace FantasyWarrior.Core.Cockcoin;

/// <summary>
/// Short, stable codes for what earned a <see cref="Data.Entities.CockcoinAward"/>
/// — never user-supplied, so unlike <c>StatKeys</c> this isn't a validated
/// whitelist, just a place to keep the strings from drifting as more ways to
/// earn cockcoin get added (cockman-concept.md's "library of bonus-entry
/// prompt types" is meant to grow here).
/// </summary>
public static class CockcoinReasons
{
    public const string TradeVote = "trade-vote";

    /// <summary>Cockcoin awarded for <see cref="TradeVote"/>.</summary>
    public const int TradeVoteAmount = 2;
}
