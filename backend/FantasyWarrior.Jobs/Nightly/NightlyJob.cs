using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Lineups;
using FantasyWarrior.Core.Periods;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Jobs.Scoring;
using FantasyWarrior.Jobs.Trades;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Nightly;

/// <summary>
/// The one nightly entry point, in the one order that is correct.
///
/// That order is load-bearing and it used to live only as step order in a
/// GitHub Actions YAML file, where nothing enforced it and reordering two
/// lines would silently corrupt a week:
///
///   1. score the current week
///   2. finalize any week whose grace period has passed
///   3. execute accepted trades, effective the next week's start
///   4. materialize next week's lineups
///
/// Trades must run after finalization (or a departing player's banked points
/// would be recomputed against a roster he already left) and before
/// materialization (or an arriving player would have no lineup row to be
/// activated in).
/// </summary>
public sealed class NightlyJob(FirestoreDb db)
{
    public async Task<int> RunAsync(
        bool commitScore, bool dryRun, int? backfillFrom = null,
        DateTimeOffset? nowOverride = null, CancellationToken ct = default)
    {
        // The simulation cursor when one is running, the real clock otherwise.
        var simulation = await new SimulationClock(db).StateAsync(ct);
        var now = nowOverride
            ?? (simulation is null ? DateTimeOffset.UtcNow : SimulationClock.FromAsOfDate(simulation.AsOfDate));
        var lastStatDate = PoolClock.LastStatDateIso(now);
        var simulated = nowOverride is not null || simulation is not null;
        Console.WriteLine($"===== nightly {PoolClock.TodayEtIso(now)} (ET)"
            + $"{(simulated ? "  [SIMULATED]" : "")}{(dryRun ? "  [DRY RUN]" : "")} =====\n");

        var finalized = 0;
        if (backfillFrom is not null)
        {
            // Replaying history has to alternate score-then-finalize week by
            // week: rollups only accumulate at finalization, so scoring several
            // weeks in a row would leave only the last one standing.
            Console.WriteLine($"[backfill] Replaying from week {backfillFrom} — one date-range query per week.\n");
            foreach (var index in await PendingIndexesAsync(backfillFrom.Value, lastStatDate, ct))
            {
                Console.WriteLine($"--- week {index} ---");
                await new PeriodRollupJob(db).RunAsync(null, commitScore, dryRun, now, index, ct);
                finalized += await FinalizeAsync(lastStatDate, dryRun, index, ct);
            }
            Console.WriteLine($"\n[backfill] {finalized} week(s) banked.\n");
        }

        Console.WriteLine("[1/4] Scoring the current week");
        await new PeriodRollupJob(db).RunAsync(null, commitScore, dryRun, now, null, ct);

        Console.WriteLine("\n[2/4] Finalizing finished weeks");
        finalized += await FinalizeAsync(lastStatDate, dryRun, null, ct);

        Console.WriteLine("\n[3/4] Executing accepted trades");
        // Only when a week actually closed. Running these every night would let
        // a trade land mid-week, which breaks the rule the whole model rests on:
        // a roster spot spans whole periods, so a player's points always belong
        // to exactly one team for any given week.
        if (finalized == 0)
            Console.WriteLine("  No period closed tonight — trades wait for the next week end.");
        else if (dryRun)
            Console.WriteLine("  (skipped in dry run)");
        else
            await new ProcessTradesJob(db).RunAsync(await NextPeriodStartAsync(lastStatDate, ct), ct);

        Console.WriteLine("\n[4/4] Materializing next week's lineups");
        await MaterializeNextAsync(lastStatDate, dryRun, ct);

        Console.WriteLine($"\n===== nightly done ({finalized} period(s) finalized) =====");
        return 0;
    }

    /// <summary>
    /// Banks every period whose grace day has passed, per league and team, then
    /// stamps the period itself finalized — last, so a crash mid-way leaves the
    /// period unfinalized and the next run simply redoes it.
    ///
    /// Each team's guard is checked and written in the same atomic document
    /// update, which is what makes re-running a no-op rather than a doubling.
    /// </summary>
    /// <summary>
    /// The start date of the week now beginning — the date an executed trade
    /// takes effect from, so the incoming player is owned for that whole week
    /// rather than from part-way through it.
    /// </summary>
    private async Task<string> NextPeriodStartAsync(string lastStatDate, CancellationToken ct)
    {
        var periods = (await db.Collection("periods").GetSnapshotAsync(ct)).Documents
            .Select(d => d.ConvertTo<Period>())
            .OrderBy(p => p.StartDate)
            .ToList();
        return periods.FirstOrDefault(p => string.CompareOrdinal(p.StartDate, lastStatDate) > 0)?.StartDate
            // Past the last week of the season: nothing further starts, so the
            // day after the last results is as close as it gets.
            ?? PoolClock.Iso(DateOnly.Parse(lastStatDate).AddDays(1));
    }

    /// <summary>Weeks ready to bank, oldest first.</summary>
    private async Task<List<int>> PendingIndexesAsync(int from, string lastStatDate, CancellationToken ct) =>
        [.. (await db.Collection("periods").GetSnapshotAsync(ct)).Documents
            .Select(d => d.ConvertTo<Period>())
            .Where(p => p.Index >= from && p.FinalizedUtc is null && PeriodScoring.ShouldFinalize(p.EndDate, lastStatDate))
            .Select(p => p.Index)
            .Distinct()
            .OrderBy(i => i)];

    private async Task<int> FinalizeAsync(string lastStatDate, bool dryRun, int? onlyIndex, CancellationToken ct)
    {
        var pending = (await db.Collection("periods").GetSnapshotAsync(ct)).Documents
            .Select(d => (Ref: d.Reference, Period: d.ConvertTo<Period>()))
            .Where(p => p.Period.FinalizedUtc is null
                && (onlyIndex is null || p.Period.Index == onlyIndex)
                && PeriodScoring.ShouldFinalize(p.Period.EndDate, lastStatDate))
            .OrderBy(p => p.Period.Season).ThenBy(p => p.Period.Index)
            .ToList();

        if (pending.Count == 0)
        {
            Console.WriteLine("  Nothing ready to finalize.");
            return 0;
        }

        var leagues = (await db.Collection("leagues").GetSnapshotAsync(ct)).Documents
            .Select(d => (d.Reference, League: d.ConvertTo<League>())).ToList();

        var done = 0;
        foreach (var (periodRef, period) in pending)
        {
            Console.WriteLine($"  W{period.Index:00} {period.Season} ({period.StartDate}..{period.EndDate})");
            foreach (var (leagueRef, league) in leagues.Where(l => l.League.Season == period.Season))
            {
                var teams = await leagueRef.Collection("teams").GetSnapshotAsync(ct);
                foreach (var teamDoc in teams.Documents)
                {
                    var team = teamDoc.ConvertTo<Team>();
                    var lineupSnap = await leagueRef.Collection("lineups")
                        .Document(Lineup.DocId(team.OwnerUsername, period.Index)).GetSnapshotAsync(ct);
                    var points = lineupSnap.Exists ? lineupSnap.ConvertTo<Lineup>().ActivePoints : 0;

                    var applied = PeriodScoring.ApplyFinalize(
                        team.FinalizedThroughPeriodIndex, period.Index, team.FinalizedScore, points);
                    if (applied is null) continue;

                    Console.WriteLine($"    {league.Name}/{team.OwnerUsername,-12} +{points,6:0.#} -> {applied.Value.FinalizedPoints:0.#}");
                    if (dryRun) continue;

                    // Value and guard in one update: they can never disagree.
                    //
                    // `score` is rewritten here too. The rollup ran earlier in
                    // this same pass and computed it from the *pre-banking*
                    // finalized total, so leaving it alone would show only the
                    // current week's points until the next run — a whole week of
                    // visibly wrong standings.
                    await teamDoc.Reference.UpdateAsync(new Dictionary<string, object>
                    {
                        ["finalizedScore"] = applied.Value.FinalizedPoints,
                        ["finalizedThroughPeriodIndex"] = applied.Value.FinalizedThroughPeriodIndex,
                        ["score"] = PeriodScoring.LiveScore(applied.Value.FinalizedPoints, team.PeriodPoints),
                    }, cancellationToken: ct);
                }
            }

            if (dryRun) continue;
            await periodRef.UpdateAsync("finalizedUtc", Timestamp.GetCurrentTimestamp(), cancellationToken: ct);
            done++;
        }
        return done;
    }

    /// <summary>
    /// Creates next week's lineup documents a week ahead, carried forward from
    /// this week and topped up.
    ///
    /// Doing it now rather than at lock time matters: the lock falls at midnight
    /// ET and this job runs hours later, so materializing at lock would leave a
    /// window where the documents don't exist and a GM is shut out of a lineup
    /// he never saw. Existing documents are never overwritten.
    /// </summary>
    private async Task MaterializeNextAsync(string lastStatDate, bool dryRun, CancellationToken ct)
    {
        var leagues = (await db.Collection("leagues").GetSnapshotAsync(ct)).Documents
            .Select(d => (d.Reference, League: d.ConvertTo<League>())).ToList();

        foreach (var (leagueRef, league) in leagues)
        {
            var periods = (await db.Collection("periods")
                    .WhereEqualTo("season", league.Season).OrderBy("index").GetSnapshotAsync(ct))
                .Documents.Select(d => d.ConvertTo<Period>()).ToList();
            if (periods.Count == 0) continue;

            var current = periods.LastOrDefault(p => string.CompareOrdinal(p.StartDate, lastStatDate) <= 0);
            var next = current is null ? periods[0] : periods.FirstOrDefault(p => p.Index == current.Index + 1);
            if (next is null) { Console.WriteLine($"  {league.Name}: season is over."); continue; }

            var slots = LineupRules.SlotsFrom(league.RuleConfig);
            var spots = await RosterSpots.RelevantToAsync(leagueRef, next.StartDate, ct);
            var byTeam = spots.Where(s => s.Spot.IsOpen).GroupBy(s => s.Spot.TeamUsername);

            var created = 0;
            foreach (var group in byTeam)
            {
                var nextRef = leagueRef.Collection("lineups").Document(Lineup.DocId(group.Key, next.Index));
                if ((await nextRef.GetSnapshotAsync(ct)).Exists) continue;

                var candidates = group.Select(s => new LineupCandidate(
                    s.Ref.Id, s.Spot.PlayerId, s.Spot.PositionGroup, s.Spot.ActivePoints, s.Spot.OpenedUtc.ToDateTime())).ToList();

                var previousSnap = current is null ? null
                    : await leagueRef.Collection("lineups").Document(Lineup.DocId(group.Key, current.Index)).GetSnapshotAsync(ct);
                var previousActive = previousSnap?.Exists == true
                    ? previousSnap.ConvertTo<Lineup>().ActiveSpotIds : [];

                var active = LineupRules.CarryForward(candidates, previousActive, slots);
                if (dryRun) { created++; continue; }

                await nextRef.SetAsync(new Lineup
                {
                    TeamUsername = group.Key,
                    PeriodIndex = next.Index,
                    Season = league.Season,
                    ActiveSpotIds = [.. active],
                    SetBy = LineupSetBy.Auto,
                }, cancellationToken: ct);
                created++;
            }
            Console.WriteLine($"  {league.Name}: W{next.Index:00} — {created} lineup(s) created"
                + (next.GameCount == 0 ? " (break week)" : ""));
        }
    }
}
