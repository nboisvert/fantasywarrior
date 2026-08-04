using FantasyWarrior.Core.Players;

namespace FantasyWarrior.Core.Tests.Players;

/// <summary>
/// Matching a name a news site wrote against the name our own table holds.
///
/// The cases below are the real ones from 2026-08-04: seven veterans between
/// contracts were stored as "R. Gudas" while both injury sources wrote "Radko
/// Gudas", so their injuries matched nothing and no roster could ever show
/// them out. And the league really does have two Sebastian Ahos, which is why
/// the fallback refuses rather than guesses.
/// </summary>
public class PlayerNameIndexTests
{
    private static PlayerNameIndex Index(params (long, string, string)[] players) => new(players);

    [Fact]
    public void ResolvesAnExactName()
    {
        var index = Index((8477939, "William", "Nylander"));
        Assert.Equal(8477939, index.Resolve("William Nylander"));
    }

    [Fact]
    public void IgnoresCaseAndAccents()
    {
        var index = Index((8480806, "Isac", "Lundeström"));
        Assert.Equal(8480806, index.Resolve("isac lundestrom"));
    }

    /// <summary>The case this class exists for.</summary>
    [Fact]
    public void ResolvesAFullNameAgainstAnAbbreviatedRow()
    {
        var index = Index((8475768, "J.", "Schwartz"));
        Assert.Equal(8475768, index.Resolve("Jaden Schwartz"));
    }

    /// <summary>And the other direction, since a source can abbreviate too.</summary>
    [Fact]
    public void ResolvesAnAbbreviatedNameAgainstAFullRow()
    {
        var index = Index((8475462, "Radko", "Gudas"));
        Assert.Equal(8475462, index.Resolve("R. Gudas"));
    }

    /// <summary>A wrong match puts another man's injury on a GM's roster,
    /// which is worse than no match at all.</summary>
    [Fact]
    public void RefusesAnAmbiguousInitial()
    {
        var index = Index((8478427, "Sebastian", "Aho"), (8480222, "Sebastian", "Aho"));
        Assert.Null(index.Resolve("S. Aho"));
    }

    [Fact]
    public void RefusesAnAmbiguousFullName()
    {
        var index = Index((111, "Sebastian", "Aho"), (222, "Sebastian", "Aho"));
        Assert.Null(index.Resolve("Sebastian Aho"));
    }

    /// <summary>An exact name still wins over an initial that would also fit —
    /// otherwise the wider net would start overruling the precise one.</summary>
    [Fact]
    public void PrefersTheExactNameOverTheInitialFallback()
    {
        var index = Index((111, "Jaden", "Schwartz"), (222, "Jordan", "Schwartz"));
        Assert.Equal(111, index.Resolve("Jaden Schwartz"));
        // "j schwartz" now covers two men, so nothing resolves through it.
        Assert.Null(index.Resolve("J. Schwartz"));
    }

    [Fact]
    public void ReturnsNullForSomebodyWeDoNotHold()
    {
        var index = Index((8477939, "William", "Nylander"));
        Assert.Null(index.Resolve("Alex Pietrangelo"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Crosby")]
    public void ReturnsNullForANameItCannotSplit(string? name) =>
        Assert.Null(Index((8471675, "Sidney", "Crosby")).Resolve(name));

    /// <summary>Two rows for the same player under one key is not ambiguity —
    /// it resolves to him.</summary>
    [Fact]
    public void TheSamePlayerListedTwice_IsNotAConflict()
    {
        var index = Index((8471675, "Sidney", "Crosby"), (8471675, "Sidney", "Crosby"));
        Assert.Equal(8471675, index.Resolve("Sidney Crosby"));
    }
}
