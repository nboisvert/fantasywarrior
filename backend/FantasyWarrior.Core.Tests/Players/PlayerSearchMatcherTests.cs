using FantasyWarrior.Core.Players;

namespace FantasyWarrior.Core.Tests.Players;

/// <summary>
/// Picking the right player out of what the NHL search endpoint answers.
///
/// The candidate sets below are real captures from 2026-08-05, taken while
/// resolving the 43 players the Mordus import could not match. They are kept
/// verbatim — including Bolduc's seven namesakes and the diacritic on
/// Brandsegg-Nygård — because every case here is one the live data actually
/// produced, not one imagined to make the matcher look good.
/// </summary>
public class PlayerSearchMatcherTests
{
    private static PlayerSearchCandidate P(long id, string name, string pos, string? team = null, bool active = true)
        => new(id, name, pos, team, active);

    // Everyone the endpoint returns for q=Bolduc. The one we want is spelled
    // Zachary; the Mordus PDF wrote Zack.
    private static readonly PlayerSearchCandidate[] Bolducs =
    [
        P(8474827, "Mathieu Bolduc", "D", active: false),
        P(8470870, "Jean-Michel Bolduc", "D", active: false),
        P(8455652, "Gerald Bolduc", "D", active: false),
        P(8468860, "Tommy Bolduc", "L", active: false),
        P(8481541, "Samuel Bolduc", "D", "OTT"),
        P(8470719, "Alexandre Bolduc", "C", active: false),
        P(8482737, "Zachary Bolduc", "R", "MTL"),
    ];

    [Fact]
    public void QueriesTheSurnameOnly()
    {
        // Querying "Zack Bolduc" whole answers Zack *Smith* — the endpoint
        // falls back to the first name, silently. This is the guard.
        Assert.Equal("Bolduc", PlayerSearchMatcher.QueryFor("Zack Bolduc"));
    }

    [Fact]
    public void KeepsCompoundSurnamesWhole()
    {
        Assert.Equal("Sandin Pellikka", PlayerSearchMatcher.QueryFor("Axel Sandin Pellikka"));
    }

    [Fact]
    public void TreatsASingleTokenAsTheSurname()
    {
        Assert.Equal("Ovechkin", PlayerSearchMatcher.QueryFor("Ovechkin"));
    }

    [Fact]
    public void SplitsTheFirstTokenOffAsTheGivenName()
    {
        var (first, last) = PlayerSearchMatcher.SplitName("Michael Brandsegg-Nygård");
        Assert.Equal("Michael", first);
        Assert.Equal("Brandsegg-Nygård", last);
    }

    [Fact]
    public void ResolvesADiminutiveAgainstTheFullGivenName()
    {
        // "Zack" against "Zachary", among six other Bolducs.
        Assert.Equal(8482737, PlayerSearchMatcher.Resolve("Zack Bolduc", Bolducs));
    }

    [Fact]
    public void ResolvesAcrossADiacritic()
    {
        PlayerSearchCandidate[] candidates = [P(8484794, "Michael Brandsegg-Nygård", "R", "DET")];
        Assert.Equal(8484794, PlayerSearchMatcher.Resolve("Michael Brandsegg-Nygard", candidates));
    }

    [Fact]
    public void ResolvesAcrossAHyphenTheSourceWroteAsASpace()
    {
        PlayerSearchCandidate[] candidates = [P(8484223, "Axel Sandin-Pellikka", "D", "DET")];
        Assert.Equal(8484223, PlayerSearchMatcher.Resolve("Axel Sandin Pellikka", candidates));
    }

    [Fact]
    public void ResolvesShortenedAndTransliteratedGivenNames()
    {
        PlayerSearchCandidate[] montembeault = [P(8478470, "Samuel Montembeault", "G", "MTL")];
        Assert.Equal(8478470, PlayerSearchMatcher.Resolve("Sam Montembeault", montembeault));

        PlayerSearchCandidate[] simashev = [P(8484386, "Dmitri Simashev", "D", "UTA")];
        Assert.Equal(8484386, PlayerSearchMatcher.Resolve("Dmitriy Simashev", simashev));
    }

    /// <summary>
    /// An unsigned free agent comes back with no team and active=false. He is
    /// still the right man — the matcher must not read "inactive" as "not him".
    /// </summary>
    [Fact]
    public void ResolvesAPlayerWhoIsOnNoTeam()
    {
        PlayerSearchCandidate[] candidates =
        [
            P(8475159, "Carl Klingberg", "L", active: false),
            P(8475906, "John Klingberg", "D", active: false),
        ];
        Assert.Equal(8475906, PlayerSearchMatcher.Resolve("John Klingberg", candidates));
    }

    [Fact]
    public void ReturnsNullWhenNobodyMatches()
    {
        Assert.Null(PlayerSearchMatcher.Resolve("Marcel Bolduc", Bolducs));
    }

    [Fact]
    public void ReturnsNullWhenTheEndpointAnsweredNothing()
    {
        Assert.Null(PlayerSearchMatcher.Resolve("Nobody Here", []));
    }

    /// <summary>
    /// The guarantee the whole job rests on. Two men of the same name resolve
    /// to neither — a wrong player written onto a keeper roster gets traded,
    /// scored and banked before anyone notices, while a missing one is
    /// obvious the moment the roster is counted.
    /// </summary>
    [Fact]
    public void RefusesToArbitrateBetweenNamesakes()
    {
        PlayerSearchCandidate[] candidates =
        [
            P(8478427, "Sebastian Aho", "C", "CAR"),
            P(8480222, "Sebastian Aho", "D", "NYI"),
        ];
        Assert.Null(PlayerSearchMatcher.Resolve("Sebastian Aho", candidates));
    }

    /// <summary>
    /// And it must not degrade on a crowded surname: "Smith" answers with 136
    /// men, one of whom is Reilly.
    /// </summary>
    [Fact]
    public void ResolvesOutOfACrowdedSurname()
    {
        var smiths = Enumerable.Range(0, 135)
            .Select(i => P(8400000 + i, $"Generic{i} Smith", "C", active: false))
            .Append(P(8475191, "Reilly Smith", "R", active: false))
            .ToArray();
        Assert.Equal(8475191, PlayerSearchMatcher.Resolve("Reilly Smith", smiths));
    }
}
