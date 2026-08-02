using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Data.Entities;

namespace FantasyWarrior.Api;

/// <summary>
/// The wire shapes, matching <c>frontend/src/api.ts</c> field for field.
///
/// That match is the whole discipline of this migration: the UI is finished and
/// must not change, so the database underneath it can be replaced entirely as
/// long as these come out identical. Anything renamed here is a frontend change
/// in disguise.
/// </summary>
public static class Dtos
{
    /// <summary>
    /// A league's scoring config in the shape the frontend has always read:
    /// the five historic values as named properties, everything else in a map.
    ///
    /// Stored as rows now (one per scored stat), which is what lets a
    /// commissioner score blocked shots without a migration — but that is an
    /// implementation detail the Rules panel never needs to learn.
    /// </summary>
    public static object RuleConfig(League league, IReadOnlyDictionary<string, double> scale)
    {
        var extras = scale
            .Where(kv => kv.Key is not (StatKeys.Goals or StatKeys.Assists
                or StatKeys.Wins or StatKeys.OtLosses or StatKeys.Shutouts))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return new
        {
            pointValues = new
            {
                goal = scale.GetValueOrDefault(StatKeys.Goals),
                assist = scale.GetValueOrDefault(StatKeys.Assists),
                goalieWin = scale.GetValueOrDefault(StatKeys.Wins),
                goalieOtLoss = scale.GetValueOrDefault(StatKeys.OtLosses),
                shutout = scale.GetValueOrDefault(StatKeys.Shutouts),
            },
            extraPointValues = extras,
            topCount = new
            {
                forwards = (int?)league.ActiveForwards,
                defense = (int?)league.ActiveDefense,
                goalies = (int?)league.ActiveGoalies,
            },
            rosterSize = new { min = league.RosterMin, max = league.RosterMax },
        };
    }

    /// <summary>
    /// One scoring week. <c>index</c> keeps its name even though the column is
    /// <c>Number</c> — the frontend reads <c>index</c>.
    /// </summary>
    public static object Period(Period period, DateTimeOffset now) => new
    {
        index = period.Number,
        startDate = period.StartDate,
        endDate = period.EndDate,
        gameCount = period.GameCount,
        locked = period.LockUtc <= now.UtcDateTime,
        finalized = period.FinalizedUtc is not null,
    };

    public static object Player(Player p) => new
    {
        id = p.PlayerId,
        name = p.FullName,
        position = p.Position,
        team = p.TeamAbbrev,
        status = p.Status,
        capHit = (long?)null,
        headshotUrl = p.HeadshotUrl,
    };
}
