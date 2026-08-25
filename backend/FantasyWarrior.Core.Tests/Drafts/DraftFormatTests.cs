using FantasyWarrior.Core.Drafts;

namespace FantasyWarrior.Core.Tests.Drafts;

public class DraftFormatTests
{
    [Fact]
    public void ShortName_IsInitialAndSurname()
    {
        Assert.Equal("N. MacKinnon", DraftFormat.ShortName("Nathan", "MacKinnon"));
    }

    [Fact]
    public void ShortName_HandlesAHyphenatedFirstName()
    {
        // Splitting FullName on a space would give "Jean-Gabriel" as a surname.
        Assert.Equal("J. Pageau", DraftFormat.ShortName("Jean-Gabriel", "Pageau"));
    }

    [Fact]
    public void ShortName_HandlesAHyphenatedSurname()
    {
        Assert.Equal("R. Nugent-Hopkins", DraftFormat.ShortName("Ryan", "Nugent-Hopkins"));
    }

    [Fact]
    public void ShortName_KeepsAnAccentedInitial()
    {
        Assert.Equal("É. Pettersson", DraftFormat.ShortName("Élias", "Pettersson"));
    }

    [Fact]
    public void ShortName_MissingFirstNameYieldsTheSurnameAlone()
    {
        // A stray "." reads as a bug on a row a GM is about to act on.
        Assert.Equal("Pettersson", DraftFormat.ShortName("", "Pettersson"));
        Assert.Equal("Pettersson", DraftFormat.ShortName(null, "Pettersson"));
    }

    [Fact]
    public void ShortName_MissingSurnameYieldsTheFirstName()
    {
        Assert.Equal("Nathan", DraftFormat.ShortName("Nathan", ""));
    }

    [Fact]
    public void ShortName_TrimsBeforeTakingTheInitial()
    {
        Assert.Equal("N. MacKinnon", DraftFormat.ShortName("  Nathan ", " MacKinnon "));
    }
}
