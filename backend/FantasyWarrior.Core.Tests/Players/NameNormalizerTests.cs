using FantasyWarrior.Core.Players;

namespace FantasyWarrior.Core.Tests.Players;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Phillip Danault", "phillip danault")]
    [InlineData("André Burakovsky", "andre burakovsky")]
    [InlineData("Alexis Lafrenière", "alexis lafreniere")]
    [InlineData("K'Andre Miller", "kandre miller")]
    [InlineData("Jean-Gabriel  Pageau ", "jeangabriel pageau")]
    [InlineData("ZACH WERENSKI", "zach werenski")]
    public void Normalize_HandlesAccentsCaseAndPunctuation(string input, string expected)
        => Assert.Equal(expected, NameNormalizer.Normalize(input));
}
