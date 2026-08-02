using FantasyWarrior.Core.Lineups;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Scores one week for every league: one <see cref="RosterAssignment"/> row per
/// (roster spot, period), carrying what that player produced in the days his
/// team actually owned him and whether the GM had him active for it.
///
/// **It writes nothing else.** No team score, no spot total, no league
/// standing — those are views over these rows. The Firestore job had to write
/// all three and keep them agreeing; deleting that duty is the single biggest
/// simplification of the migration.
///
/// Safe to re-run by construction: the current week is recomputed from scratch,
/// the (spot, period) pair is unique, and a finalized row is never touched
/// again. That last rule is the banking rule — a week's points belong
/// permanently to whoever fielded the player, so a later trade cannot move them
/// and a mid-season change to the scale does not restate the past.
/// </summary>
public sealed class PeriodRollupJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(
        int? onlyLeagueId, bool dryRun, DateTimeOffset? nowOverride = null,
        int? onlyPeriodNumber = null, CancellationToken ct = default)
    {
        var now = nowOverride ?? await new SimulationClockSql(db).NowAsync(ct);
        var lastStatDate = PoolClock.LastStatDate(now);

        Console.WriteLine($"=== period-rollup{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"Last fully-synced game day: {lastStatDate:yyyy-MM-dd}\n");

        var leagues = await db.Leagues
            .Where(l => onlyLeagueId == null || l.LeagueId == onlyLeagueId)
            .ToListAsync(ct);

        foreach (var league in leagues)
        {
            var periods = await db.Periods
                .Where(p => p.Season == league.Season)
                .OrderBy(p => p.Number)
                .ToListAsync(ct);
            if (periods.Count == 0)
            {
                Console.WriteLine($"--- {league.Name}: no calendar for {league.Season}, skipped\n");
                continue;
            }

            var current = periods.LastOrDefault(p => p.StartDate <= lastStatDate);
            if (current is null)
            {
                Console.WriteLine($"--- {league.Name}: season hasn't started, skipped\n");
                continue;
            }

            var period = onlyPeriodNumber is null
                ? current
                : periods.FirstOrDefault(p => p.Number == onlyPeriodNumber);
            if (period is null)
            {
                Console.WriteLine($"--- {league.Name}: week {onlyPeriodNumber} not in this season, skipped\n");
                continue;
            }

            Console.WriteLine($"--- {league.Name} [{league.JoinCode}]  W{period.Number:00}  "
                + $"{period.StartDate:yyyy-MM-dd} -> {period.EndDate:yyyy-MM-dd}"
                + (period.GameCount == 0 ? "  (break week)" : $"  {period.GameCount} games"));

            if (period.FinalizedUtc is not null)
            {
                // Banked weeks are immutable. `recompute` is the deliberate way
                // back in; silently rescoring here would restate points teams
                // already own.
                Console.WriteLine("    already banked — untouched.\n");
                continue;
            }

            await ScoreLeagueWeekAsync(league, period, periods, lastStatDate, dryRun, ct);
            Console.WriteLine();
        }
        return 0;
    }

    private async Task ScoreLeagueWeekAsync(
        League league, Period period, List<Period> periods, DateOnly lastStatDate,
        bool dryRun, CancellationToken ct)
    {
        var scale = await ScoringScaleAsync(league.LeagueId, ct);
        var slots = new LineupSlots(league.ActiveForwards, league.ActiveDefense, league.ActiveGoalies);

        // Every spot owning any part of this week: open ones, plus ones that
        // closed during it. Forgetting the second half is the bug that made
        // every team's score sag each night until the week finalized — under
        // Firestore it needed two queries and a dedupe, here it is one
        // predicate the index covers.
        var spots = await db.RosterSpots
            .Where(s => s.LeagueId == league.LeagueId
                        && s.StartDate <= period.EndDate
                        && (s.EndDate == null || s.EndDate >= period.StartDate))
            .ToListAsync(ct);
        if (spots.Count == 0)
        {
            Console.WriteLine("    no roster spots in range.");
            return;
        }

        // THE query: the week's game lines once, shared by every team. The cap
        // is lastStatDate because scoring a day whose boxscores are not in yet
        // would bank a zero for it and never come back.
        var to = period.EndDate <= lastStatDate ? period.EndDate : lastStatDate;
        var playerIds = spots.Select(s => s.PlayerId).Distinct().ToList();
        var lines = await db.PlayerGameStats
            .Where(l => l.GameDate >= period.StartDate && l.GameDate <= to
                        && l.Season == league.Season
                        && l.GameType == GameType.RegularSeason
                        && playerIds.Contains(l.PlayerId))
            .ToListAsync(ct);
        var byPlayer = lines.ToLookup(l => l.PlayerId);
        Console.WriteLine($"    {spots.Count} spots, {lines.Count} game lines in {period.StartDate:MM-dd}..{to:MM-dd}");

        var existing = await db.RosterAssignments
            .Where(a => a.PeriodId == period.PeriodId
                        && spots.Select(s => s.RosterSpotId).Contains(a.RosterSpotId))
            .ToDictionaryAsync(a => a.RosterSpotId, ct);

        var previous = periods.LastOrDefault(p => p.Number < period.Number);
        var previousActive = previous is null
            ? []
            : (await db.RosterAssignments
                .Where(a => a.PeriodId == previous.PeriodId && a.IsActive
                            && spots.Select(s => s.RosterSpotId).Contains(a.RosterSpotId))
                .Select(a => a.RosterSpotId)
                .ToListAsync(ct)).ToHashSet();

        foreach (var teamSpots in spots.GroupBy(s => s.TeamId))
        {
            var team = teamSpots.Key;
            var candidates = teamSpots
                .Select(s => new LineupCandidate(
                    SpotId: s.RosterSpotId.ToString(),
                    PlayerId: s.PlayerId,
                    PositionGroup: s.PositionGroup,
                    SeasonPointsToDate: 0,
                    ActivatedUtc: s.OpenedUtc))
                .ToList();

            // What the GM chose, if he chose. Otherwise last week's set carried
            // forward and topped up — a GM on holiday keeps his lineup rather
            // than scoring zero, which in a pool between friends is the
            // difference between standings that mean something and standings
            // that do not.
            var submitted = await db.TeamPeriodLineups
                .FirstOrDefaultAsync(l => l.TeamId == team && l.PeriodId == period.PeriodId, ct);
            var chosen = submitted is not null && submitted.SetBy != Data.Entities.LineupSetBy.Auto
                ? existing.Values.Where(a => a.IsActive).Select(a => a.RosterSpotId.ToString()).ToList()
                : null;

            IReadOnlySet<string> active = chosen is { Count: > 0 }
                ? LineupRules.LegalActiveSet(candidates, chosen, slots)
                : LineupRules.CarryForward(
                    candidates,
                    [.. teamSpots.Where(s => previousActive.Contains(s.RosterSpotId)).Select(s => s.RosterSpotId.ToString())],
                    slots);

            double activePoints = 0, benchPoints = 0;
            foreach (var spot in teamSpots)
            {
                var window = StatWindow.Intersect(
                    period.StartDate, period.EndDate, spot.StartDate, spot.EndDate, lastStatDate);

                var isActive = active.Contains(spot.RosterSpotId.ToString());
                var stats = window is null
                    ? StatLine.Empty
                    : StatLine.Sum(byPlayer[spot.PlayerId]
                        .Where(l => l.GameDate >= window.Value.From && l.GameDate <= window.Value.To)
                        .Select(StatColumns.ToStatLine));
                var points = stats.Score(scale);

                if (isActive) activePoints += points; else benchPoints += points;
                if (dryRun) continue;

                if (existing.TryGetValue(spot.RosterSpotId, out var row))
                {
                    if (row.IsFinalized) continue;   // banked: never recomputed
                    Fill(row, stats, points, isActive, window, period);
                }
                else
                {
                    var fresh = new RosterAssignment
                    {
                        RosterSpotId = spot.RosterSpotId,
                        PeriodId = period.PeriodId,
                    };
                    Fill(fresh, stats, points, isActive, window, period);
                    db.RosterAssignments.Add(fresh);
                }
            }

            // Record that the app picked this lineup, so the UI can say so.
            if (!dryRun && submitted is null)
                db.TeamPeriodLineups.Add(new TeamPeriodLineup
                {
                    TeamId = team,
                    PeriodId = period.PeriodId,
                    SetBy = Data.Entities.LineupSetBy.Auto,
                    SubmittedUtc = DateTime.UtcNow,
                });

            Console.WriteLine($"      team {team,-4} active {activePoints,7:0.#}  bench {benchPoints,6:0.#}"
                + $"   ({active.Count} of {slots.Total} slots)");
        }

        if (!dryRun) await db.SaveChangesAsync(ct);
    }

    private static void Fill(
        RosterAssignment row, StatLine stats, double points, bool isActive,
        DateWindow? window, Period period)
    {
        StatColumns.Apply(row, stats);
        row.FantasyPoints = points;
        row.IsActive = isActive;
        // A spot that owns no day of this week still gets a row, at zero: its
        // absence and a genuine zero are different things, and the row is what
        // lets "he was on the bench all week" be answered without a join to
        // nothing.
        row.EffectiveFrom = window?.From ?? period.StartDate;
        row.EffectiveTo = window?.To ?? period.StartDate;
        row.ScoredUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// A league's scale as the engine consumes it: a map of stat name to value.
    /// Rows in, map out — which is what lets a commissioner score blocked shots
    /// without a schema change.
    /// </summary>
    public static async Task<Dictionary<string, double>> ScoringScaleAsync(
        FantasyWarriorDbContext db, int leagueId, CancellationToken ct = default) =>
        await db.LeagueScoringRules
            .Where(r => r.LeagueId == leagueId)
            .ToDictionaryAsync(r => r.StatKey, r => r.PointValue, ct);

    private Task<Dictionary<string, double>> ScoringScaleAsync(int leagueId, CancellationToken ct) =>
        ScoringScaleAsync(db, leagueId, ct);
}
