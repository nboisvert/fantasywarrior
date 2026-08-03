namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One event that earned a user some amount of cockcoin — a ledger, not a
/// mutable running total, same reasoning as everywhere else in this schema
/// (see Team's remarks): a balance derived by SUM over an honest event log
/// can never drift out of agreement with itself, and this doubles as a free
/// audit trail ("why do I have this many") for what is otherwise a joke
/// currency with real numbers behind it.
///
/// See <c>.claude/doc/cockman-concept.md</c> for what cockcoin is and isn't.
/// </summary>
public sealed class CockcoinAward
{
    public long CockcoinAwardId { get; set; }

    public int UserId { get; set; }

    public int Amount { get; set; }

    /// <summary>What earned it, e.g. "trade-vote" — a short, stable code, not
    /// display text.</summary>
    public required string Reason { get; set; }

    public DateTime AwardedUtc { get; set; }

    public User? User { get; set; }
}
