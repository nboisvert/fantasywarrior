using FantasyWarrior.Core.Seasons;

namespace FantasyWarrior.Core.Tests.Seasons;

public class SeasonBoundsTests
{
    private static SeasonWindow Window(string start, string end) =>
        new(DateOnly.Parse(start), DateOnly.Parse(end));

    [Fact]
    public void Resolve_WithNeitherSource_IsNull()
    {
        // The only case period-init has to refuse: nobody knows when the season runs.
        Assert.Null(SeasonBounds.Resolve(null, null));
    }

    [Fact]
    public void Resolve_WithDeclaredOnly_UsesIt()
    {
        // The reason this table exists: next season's calendar before a game is imported.
        var declared = Window("2026-10-06", "2027-04-15");

        Assert.Equal(declared, SeasonBounds.Resolve(declared, null));
    }

    [Fact]
    public void Resolve_WithObservedOnly_UsesIt()
    {
        // A season already imported that nobody ever declared — every league
        // before this table existed.
        var observed = Window("2025-10-07", "2026-04-16");

        Assert.Equal(observed, SeasonBounds.Resolve(null, observed));
    }

    [Fact]
    public void Resolve_MidSeason_KeepsTheDeclaredEnd()
    {
        // Only the games played so far are imported, so the observed end is
        // today. Stopping the calendar there would leave the rest of the season
        // with no weeks at all.
        var resolved = SeasonBounds.Resolve(
            declared: Window("2026-10-06", "2027-04-15"),
            observed: Window("2026-10-07", "2026-12-22"));

        Assert.Equal(Window("2026-10-06", "2027-04-15"), resolved);
    }

    [Fact]
    public void Resolve_WithAGamePastTheDeclaredEnd_StretchesToCoverIt()
    {
        // The failure this function exists to prevent: a rescheduled game
        // outside every period would score for nobody, and nothing would say so.
        var resolved = SeasonBounds.Resolve(
            declared: Window("2026-10-06", "2027-04-15"),
            observed: Window("2026-10-06", "2027-04-18"));

        Assert.Equal(Window("2026-10-06", "2027-04-18"), resolved);
    }

    [Fact]
    public void Resolve_WithAGameBeforeTheDeclaredStart_StretchesBackwards()
    {
        // Same rule at the other end — the Global Series opener abroad is the
        // real shape of this.
        var resolved = SeasonBounds.Resolve(
            declared: Window("2026-10-06", "2027-04-15"),
            observed: Window("2026-10-01", "2027-04-15"));

        Assert.Equal(Window("2026-10-01", "2027-04-15"), resolved);
    }

    [Fact]
    public void Resolve_IsSymmetric_NeitherSourceWinsByBeingFirst()
    {
        var a = Window("2026-10-01", "2027-04-15");
        var b = Window("2026-10-06", "2027-04-18");

        Assert.Equal(SeasonBounds.Resolve(a, b), SeasonBounds.Resolve(b, a));
    }

    [Fact]
    public void Resolve_WithAgreeingSources_ChangesNothing()
    {
        var both = Window("2025-10-07", "2026-04-16");

        Assert.Equal(both, SeasonBounds.Resolve(both, both));
    }
}
