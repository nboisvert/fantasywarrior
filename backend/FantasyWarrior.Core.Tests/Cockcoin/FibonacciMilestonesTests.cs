using FantasyWarrior.Core.Cockcoin;

namespace FantasyWarrior.Core.Tests.Cockcoin;

public class FibonacciMilestonesTests
{
    // --- IsFibonacci ---

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(13)]
    [InlineData(21)]
    [InlineData(144)]
    public void IsFibonacci_TrueForFibonacciNumbers(long n)
    {
        Assert.True(FibonacciMilestones.IsFibonacci(n));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(100)]
    public void IsFibonacci_FalseForNonFibonacciNumbers(long n)
    {
        Assert.False(FibonacciMilestones.IsFibonacci(n));
    }

    [Fact]
    public void IsFibonacci_FalseForNegativeNumbers()
    {
        Assert.False(FibonacciMilestones.IsFibonacci(-5));
    }

    // --- RewardForCount ---

    [Fact]
    public void RewardForCount_FirstMessageIsWorthMore()
    {
        Assert.Equal(FibonacciMilestones.FirstMilestoneAmount, FibonacciMilestones.RewardForCount(1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(13)]
    [InlineData(21)]
    public void RewardForCount_LaterMilestonesAreFlat(long count)
    {
        Assert.Equal(FibonacciMilestones.MilestoneAmount, FibonacciMilestones.RewardForCount(count));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(100)]
    public void RewardForCount_NullOffTheMilestoneCurve(long count)
    {
        Assert.Null(FibonacciMilestones.RewardForCount(count));
    }

    [Fact]
    public void RewardForCount_NullBelowOne()
    {
        Assert.Null(FibonacciMilestones.RewardForCount(0));
    }
}
