using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// Shared field-map builder for writing an <see cref="Assignment"/>'s
/// per-stint stats — used both by the nightly refresh of open assignments
/// (ScoreCalcJob) and the one-time freeze snapshot taken when an assignment
/// closes (the roster-remove API endpoint). Kept in Core (not Jobs) so the
/// API project can reuse it without depending on the Jobs console project.
/// </summary>
public static class AssignmentStats
{
    public static Dictionary<string, object> ToFieldMap(PlayerRawTotals totals, double fantasyPoints, Timestamp updatedUtc) =>
        new()
        {
            ["gamesPlayed"] = totals.GamesPlayed,
            ["goals"] = totals.Goals,
            ["assists"] = totals.Assists,
            ["plusMinus"] = totals.PlusMinus,
            ["pim"] = totals.Pim,
            ["shots"] = totals.Shots,
            ["hits"] = totals.Hits,
            ["blockedShots"] = totals.BlockedShots,
            ["wins"] = totals.Wins,
            ["otLosses"] = totals.OtLosses,
            ["shutouts"] = totals.Shutouts,
            ["goalsAgainst"] = totals.GoalsAgainst,
            ["saves"] = totals.Saves,
            ["shotsAgainst"] = totals.ShotsAgainst,
            ["fantasyPoints"] = fantasyPoints,
            ["statsUpdatedUtc"] = updatedUtc,
        };
}
