using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Rules;

/// <summary>
/// What a league plays by before anyone configures anything.
///
/// These reproduce what creating a league produced before rules moved into one
/// document: the five historic point values, no cap, no bounds, no draft, no
/// protections. A new league is deliberately not given Les Mordus' numbers —
/// those are one pool's rules, not a default, and they live in
/// <c>mordus.md</c>.
/// </summary>
public static class RuleSetDefaults
{
    /// <summary>
    /// The shape of a stored document. Bump only when an existing document has
    /// to be converted on read; adding a property with a sensible default is not
    /// a version change, since an older document simply deserializes to it.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The five values every league has always started with: a point per goal
    /// and per assist, two per goalie win, one per goalie overtime loss, and
    /// shutouts off.
    /// </summary>
    public static Dictionary<string, double> StartingScale() => new()
    {
        [StatKeys.Goals] = 1,
        [StatKeys.Assists] = 1,
        [StatKeys.Wins] = 2,
        [StatKeys.OtLosses] = 1,
        [StatKeys.Shutouts] = 0,
    };

    /// <summary>A brand new league's rules.</summary>
    public static RuleSet ForNewLeague() => new()
    {
        // The one place besides a real save that stamps a version, which is what
        // separates "a league that plays the defaults" from "a document nobody
        // has written". See RuleSet.Version.
        Version = CurrentVersion,
        Scoring = new ScoringConfig { Values = StartingScale() },
    };
}
