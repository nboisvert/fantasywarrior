using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Data.Entities;

namespace FantasyWarrior.Data;

/// <summary>
/// One player's regular-season counting stats, already summed. A record rather
/// than the <c>vPlayerSeasonStats</c> view type so the bounded (as of a
/// simulated day) and unbounded paths return the same shape.
/// </summary>
public sealed record SeasonTotals(
    long PlayerId, int GamesPlayed, int Goals, int Assists, int PlusMinus, int Pim,
    int Shots, int Hits, int BlockedShots, int Wins, int OtLosses, int Shutouts,
    int GoalsAgainst, int Saves, int ShotsAgainst);

/// <summary>
/// The one adapter between how stats are <b>stored</b> (typed columns, so they
/// can be queried, indexed and summed by the database) and how they are
/// <b>scored</b> (a <c>StatKeys</c>-keyed map, so a commissioner can put a value
/// on any stat without a schema change).
///
/// Both representations earn their place, and keeping the translation in one
/// file is what stops them drifting: adding a stat means touching this, the
/// entity, and <c>StatKeys</c> — and nothing else.
/// </summary>
public static class StatColumns
{
    /// <summary>
    /// One game line as scorable stats. Counts the game itself as
    /// <c>gamesPlayed</c>, so a league can legitimately score "1 point per game
    /// played" with no code change.
    /// </summary>
    public static StatLine ToStatLine(PlayerGameStat line) => new(new Dictionary<string, int>
    {
        [StatKeys.GamesPlayed] = 1,
        [StatKeys.Goals] = line.Goals ?? 0,
        [StatKeys.Assists] = line.Assists ?? 0,
        [StatKeys.PlusMinus] = line.PlusMinus ?? 0,
        [StatKeys.Pim] = line.Pim,
        [StatKeys.Shots] = line.Shots ?? 0,
        [StatKeys.Hits] = line.Hits ?? 0,
        [StatKeys.BlockedShots] = line.BlockedShots ?? 0,
        [StatKeys.Wins] = line.Decision == "W" ? 1 : 0,
        [StatKeys.OtLosses] = line.OtLoss == true ? 1 : 0,
        [StatKeys.Shutouts] = line.Shutout == true ? 1 : 0,
        [StatKeys.GoalsAgainst] = line.GoalsAgainst ?? 0,
        [StatKeys.Saves] = line.Saves ?? 0,
        [StatKeys.ShotsAgainst] = line.ShotsAgainst ?? 0,
    });

    /// <summary>
    /// A whole season's totals as a scorable line, so a league's own scale can
    /// be applied to a season the same way it is applied to one week — which is
    /// what lets a goalie rank against a skater on the free-agent board.
    /// </summary>
    public static StatLine ToStatLine(SeasonTotals t) => new(new Dictionary<string, int>
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

    /// <summary>Reads an assignment's stored columns back as a scorable line.</summary>
    public static StatLine ToStatLine(RosterAssignment a) => new(new Dictionary<string, int>
    {
        [StatKeys.GamesPlayed] = a.GamesPlayed,
        [StatKeys.Goals] = a.Goals,
        [StatKeys.Assists] = a.Assists,
        [StatKeys.PlusMinus] = a.PlusMinus,
        [StatKeys.Pim] = a.Pim,
        [StatKeys.Shots] = a.Shots,
        [StatKeys.Hits] = a.Hits,
        [StatKeys.BlockedShots] = a.BlockedShots,
        [StatKeys.Wins] = a.Wins,
        [StatKeys.OtLosses] = a.OtLosses,
        [StatKeys.Shutouts] = a.Shutouts,
        [StatKeys.GoalsAgainst] = a.GoalsAgainst,
        [StatKeys.Saves] = a.Saves,
        [StatKeys.ShotsAgainst] = a.ShotsAgainst,
    });

    /// <summary>
    /// Writes a scored line onto an assignment's columns. Every key is assigned,
    /// including zeros — a partial write would leave last night's numbers behind
    /// for any stat the player didn't record this week.
    /// </summary>
    public static void Apply(RosterAssignment a, StatLine stats)
    {
        a.GamesPlayed = stats[StatKeys.GamesPlayed];
        a.Goals = stats[StatKeys.Goals];
        a.Assists = stats[StatKeys.Assists];
        a.PlusMinus = stats[StatKeys.PlusMinus];
        a.Pim = stats[StatKeys.Pim];
        a.Shots = stats[StatKeys.Shots];
        a.Hits = stats[StatKeys.Hits];
        a.BlockedShots = stats[StatKeys.BlockedShots];
        a.Wins = stats[StatKeys.Wins];
        a.OtLosses = stats[StatKeys.OtLosses];
        a.Shutouts = stats[StatKeys.Shutouts];
        a.GoalsAgainst = stats[StatKeys.GoalsAgainst];
        a.Saves = stats[StatKeys.Saves];
        a.ShotsAgainst = stats[StatKeys.ShotsAgainst];
    }
}
