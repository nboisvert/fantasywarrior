using FantasyWarrior.Core.Stats;

namespace FantasyWarrior.Core.Scoring;

/// <summary>
/// Bridges between <see cref="StatLine"/> and the two older stat shapes it is
/// replacing, so both can coexist while the scoring refactor lands commit by
/// commit instead of in one unreviewable change.
///
/// <c>PlayerSeasonStats</c> keeps its flat on-disk fields deliberately: it is a
/// cache of ~1,000 documents rebuilt only from a full-season rescan (~49k reads,
/// right at the Firestore free-tier daily cap), so converting its stored shape
/// would cost exactly the kind of bulk reload this design is meant to avoid.
/// It converts in memory instead, for free.
/// </summary>
public static class StatLineAdapters
{
    public static StatLine ToStatLine(this PlayerRawTotals t) => new(new Dictionary<string, int>
    {
        [StatKeys.GamesPlayed] = t.GamesPlayed,
        [StatKeys.Goals] = t.Goals,
        [StatKeys.Assists] = t.Assists,
        [StatKeys.PlusMinus] = t.PlusMinus,
        [StatKeys.Pim] = t.Pim,
        [StatKeys.Shots] = t.Shots,
        [StatKeys.Hits] = t.Hits,
        [StatKeys.BlockedShots] = t.BlockedShots,
        [StatKeys.Wins] = t.Wins,
        [StatKeys.OtLosses] = t.OtLosses,
        [StatKeys.Shutouts] = t.Shutouts,
        [StatKeys.GoalsAgainst] = t.GoalsAgainst,
        [StatKeys.Saves] = t.Saves,
        [StatKeys.ShotsAgainst] = t.ShotsAgainst,
    });

    /// <summary>
    /// Back to the legacy record. Named rather than positional on purpose —
    /// <c>PlayerRawTotals</c>'s constructor order is load-bearing and two
    /// existing call sites already build it positionally.
    /// </summary>
    public static PlayerRawTotals ToRawTotals(this StatLine s) => new(
        GamesPlayed: s[StatKeys.GamesPlayed],
        Goals: s[StatKeys.Goals],
        Assists: s[StatKeys.Assists],
        Wins: s[StatKeys.Wins],
        OtLosses: s[StatKeys.OtLosses],
        Shutouts: s[StatKeys.Shutouts],
        PlusMinus: s[StatKeys.PlusMinus],
        Pim: s[StatKeys.Pim],
        Shots: s[StatKeys.Shots],
        Hits: s[StatKeys.Hits],
        BlockedShots: s[StatKeys.BlockedShots],
        GoalsAgainst: s[StatKeys.GoalsAgainst],
        Saves: s[StatKeys.Saves],
        ShotsAgainst: s[StatKeys.ShotsAgainst]);

    public static StatLine ToStatLine(this PlayerSeasonStats c) => new(new Dictionary<string, int>
    {
        [StatKeys.GamesPlayed] = c.GamesPlayed,
        [StatKeys.Goals] = c.Goals,
        [StatKeys.Assists] = c.Assists,
        [StatKeys.PlusMinus] = c.PlusMinus,
        [StatKeys.Pim] = c.Pim,
        [StatKeys.Shots] = c.Shots,
        [StatKeys.Hits] = c.Hits,
        [StatKeys.BlockedShots] = c.BlockedShots,
        [StatKeys.Wins] = c.Wins,
        [StatKeys.OtLosses] = c.OtLosses,
        [StatKeys.Shutouts] = c.Shutouts,
        [StatKeys.GoalsAgainst] = c.GoalsAgainst,
        [StatKeys.Saves] = c.Saves,
        [StatKeys.ShotsAgainst] = c.ShotsAgainst,
    });
}
