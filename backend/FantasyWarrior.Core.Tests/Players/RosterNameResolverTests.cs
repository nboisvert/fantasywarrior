using FantasyWarrior.Core.Players;

namespace FantasyWarrior.Core.Tests.Players;

/// <summary>
/// Resolving a pool tool's roster names to our players — and refusing to guess
/// between real candidates.
/// </summary>
public class RosterNameResolverTests
{
    private static readonly RosterNameResolver Resolver = new(
    [
        new(8482116, "Tim", "Stützle", "C", "OTT"),
        new(8480012, "Elias", "Pettersson", "C", "VAN"),
        new(8483678, "Elias", "Pettersson", "D", "VAN"),
        new(8478483, "Mitch", "Marner", "R", "VGK"),
        new(8476453, "Nikita", "Kucherov", "R", "TBL"),
        new(8481533, "Kasperi", "Kapanen", "R", "STL"),
        new(8482775, "Oliver", "Kapanen", "C", "MTL"),
        new(8477492, "Nathan", "MacKinnon", "C", "COL"),
        new(8481557, "Matthew", "Boldy", "L", "MIN"),
    ]);

    [Fact]
    public void AnExactNameResolvesWithoutANote()
    {
        Assert.Equal(new NameResolution(8477492, null), Resolver.Resolve(new("Nathan", "MacKinnon", "COL")));
    }

    [Fact]
    public void DiacriticsAreIgnored()
    {
        Assert.Equal(8482116, Resolver.Resolve(new("Tim", "Stutzle", "OTT")).PlayerId);
    }

    [Fact]
    public void ASwappedNomPrenomIsMatchedAndReported()
    {
        var r = Resolver.Resolve(new("Marner", "Mitch"));

        Assert.Equal(8478483, r.PlayerId);
        Assert.Contains("swapped", r.Note);
    }

    [Fact]
    public void AShortenedFirstNameOnAnExactLastNameMatches()
    {
        var r = Resolver.Resolve(new("Matt", "Boldy", "MIN"));

        Assert.Equal(8481557, r.PlayerId);
        Assert.NotNull(r.Note);
    }

    [Fact]
    public void TwoPlayersSharingASurnameAreNeverMerged()
    {
        Assert.Equal(8482775, Resolver.Resolve(new("Oliver", "Kapanen")).PlayerId);
        Assert.Equal(8481533, Resolver.Resolve(new("Kasperi", "Kapanen")).PlayerId);
    }

    [Fact]
    public void AnIdenticalFullNameWithNoUsableHintIsRefused()
    {
        Assert.Null(Resolver.Resolve(new("Elias", "Pettersson", "VAN")).PlayerId);
        Assert.Null(Resolver.Resolve(new("Elias", "Pettersson")).PlayerId);
    }

    [Fact]
    public void AnExplicitIdSkipsResolutionButMustExist()
    {
        Assert.Equal(8483678, Resolver.Resolve(new("Elias", "Pettersson", "VAN", 8483678)).PlayerId);
        Assert.Null(Resolver.Resolve(new("Elias", "Pettersson", "VAN", 1)).PlayerId);
    }

    [Fact]
    public void AnUnknownNameIsRefused()
    {
        Assert.Null(Resolver.Resolve(new("Tynan", "Lawrence", "STL")).PlayerId);
    }

    [Theory]
    [InlineData("TB", "TBL")]
    [InlineData("NJ", "NJD")]
    [InlineData("LA", "LAK")]
    [InlineData("SJ", "SJS")]
    [InlineData("WIN", "WPG")]
    [InlineData("MTL", "MTL")]
    public void PoolExpertTeamCodesMapOntoTheNhls(string hint, string expected)
    {
        Assert.Equal(expected, RosterNameResolver.NhlAbbrev(hint));
    }

    [Fact]
    public void TheTeamHintSplitsAnExactNameTie()
    {
        var resolver = new RosterNameResolver(
        [
            new(1, "Sebastian", "Aho", "C", "CAR"),
            new(2, "Sebastian", "Aho", "D", "NYI"),
        ]);

        Assert.Equal(2, resolver.Resolve(new("Sebastian", "Aho", "NYI")).PlayerId);
    }
}
