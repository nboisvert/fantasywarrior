namespace FantasyWarrior.Core.Cockcoin;

/// <summary>
/// The milestone curve behind <see cref="CockcoinReasons.ChatMessageMilestone"/>
/// and <see cref="CockcoinReasons.TradeOfferMilestone"/>: a running count (the
/// Nth message in a chat room, the Nth trade offer to one counterparty) earns
/// cockcoin exactly when it lands on a Fibonacci number. Pure — no DB, no
/// clock — so the milestone math is unit-testable on its own.
/// </summary>
public static class FibonacciMilestones
{
    public const int FirstMilestoneAmount = 5;
    public const int MilestoneAmount = 2;

    /// <summary>
    /// True iff <paramref name="n"/> is a Fibonacci number (0, 1, 1, 2, 3, 5,
    /// 8, 13, ...). O(1): n is Fibonacci iff 5n²+4 or 5n²-4 is a perfect square.
    /// </summary>
    public static bool IsFibonacci(long n)
    {
        if (n < 0) return false;
        return IsPerfectSquare(5 * n * n + 4) || IsPerfectSquare(5 * n * n - 4);
    }

    /// <summary>
    /// The cockcoin reward for a running count landing exactly on a Fibonacci
    /// milestone, or null if this count isn't one. 5 CK the first time (count
    /// == 1), 2 CK every later Fibonacci count (2, 3, 5, 8, 13, ...). Counts
    /// below 1 never reward.
    /// </summary>
    public static int? RewardForCount(long count)
    {
        if (count < 1 || !IsFibonacci(count)) return null;
        return count == 1 ? FirstMilestoneAmount : MilestoneAmount;
    }

    private static bool IsPerfectSquare(long value)
    {
        if (value < 0) return false;
        var root = (long)Math.Sqrt(value);
        for (var candidate = Math.Max(0, root - 1); candidate <= root + 1; candidate++)
            if (candidate * candidate == value)
                return true;
        return false;
    }
}
