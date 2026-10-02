using FantasyWarrior.Core.Drafts;

namespace FantasyWarrior.Core.Tests.Drafts;

public class DraftPickYearsTests
{
    [Fact]
    public void OneYearAheadIsJustTheNextDraft()
    {
        Assert.Equal([2027], DraftPickYears.Ahead(2027, 1));
    }

    [Fact]
    public void TwoYearsAheadIsTheNextDraftAndTheOneAfter()
    {
        // Les Mordus trade a year further out than the next draft.
        Assert.Equal([2027, 2028], DraftPickYears.Ahead(2027, 2));
    }

    [Fact]
    public void ANonPositiveWindowStillHoldsTheNextDraft()
    {
        Assert.Equal([2027], DraftPickYears.Ahead(2027, 0));
    }
}
