using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Lineups;
using FantasyWarrior.Core.Periods;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Stats;
using FantasyWarrior.Core.Time;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Scoring;

/// <summary>
/// Scores the current period for every league, then rolls the result up
/// through lineup -> roster spot -> team.
///
/// This replaces ScoreCalcJob's cost model entirely. The old job re-read every
/// rostered player's whole career (a `playerGameStats where playerId == X`
/// query with no season filter), twice, for every league — roughly 90,000
/// reads a night for a single nine-team league, against a 50,000/day free
/// tier, growing with every league added.
///
/// Here, one date-range query over the current week returns every player's
/// lines at once and serves every league from the same in-memory set. Cost is
/// ~2,500 reads plus a couple of hundred per league, and it does not grow as
/// the season goes on because finished periods are never re-read.
///
/// **Shadow mode (the default).** Writes lineups, spot rollups and the new
/// team fields, but never touches `score`, so the live app keeps showing the
/// legacy number while this is observed for a few nights. --commit-score flips
/// it.
///
/// Safe to re-run: the current period is always recomputed from scratch rather
/// than accumulated, document ids are deterministic, and banking a finished
/// period is guarded by PeriodScoring.ApplyFinalize.
/// </summary>
public sealed class PeriodRollupJob(FirestoreDb db)
{
    private const int BatchLimit = 450;

    private int _reads;
    private int _writes;

    public async Task<int> RunAsync(
        string? onlyLeagueId, bool commitScore, bool dryRun, DateTimeOffset? nowOverride = null, CancellationToken ct = default)
    {
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var lastStatDate = PoolClock.LastStatDateIso(now);

        Console.WriteLine($"=== period-rollup{(dryRun ? "  [DRY RUN]" : "")}{(commitScore ? "  [COMMITTING SCORE]" : "  [shadow]")} ===");
        Console.WriteLine($"Last fully-synced game day: {lastStatDate}\n");

        var leagues = onlyLeagueId is null
            ? (await Read(db.Collection("leagues").GetSnapshotAsync(ct))).Documents.ToList()
            : [await ReadDoc(db.Collection("leagues").Document(onlyLeagueId).GetSnapshotAsync(ct))];

        // Game lines are fetched once per distinct season and shared by every
        // league in it -- the whole point of a global period calendar.
        var linesBySeason = new Dictionary<string, ILookup<long, PlayerGameStats>>();
        var periodsBySeason = new Dictionary<string, IReadOnlyList<Period>>();

        foreach (var leagueSnap in leagues.Where(l => l.Exists))
        {
            var league = leagueSnap.ConvertTo<League>();
            Console.WriteLine($"--- {league.Name} [{leagueSnap.Id}] season {league.Season} ---");

            if (!periodsBySeason.TryGetValue(league.Season, out var periods))
                periodsBySeason[league.Season] = periods = await LoadPeriodsAsync(league.Season, ct);
            if (periods.Count == 0)
            {
                Console.WriteLine("  No period calendar for this season — run period-init first. Skipped.\n");
                continue;
            }

            var current = periods.LastOrDefault(p => string.CompareOrdinal(p.StartDate, lastStatDate) <= 0);
            if (current is null)
            {
                Console.WriteLine($"  Season hasn't started yet (first period {periods[0].StartDate}). Skipped.\n");
                continue;
            }
            Console.WriteLine($"  Current period: W{current.Index:00}  {current.StartDate} -> {current.EndDate}"
                + (current.GameCount == 0 ? "  (break week, no games)" : $"  {current.GameCount} games"));

            if (!linesBySeason.TryGetValue(league.Season, out var lines))
                linesBySeason[league.Season] = lines = await LoadWeekLinesAsync(league.Season, current, lastStatDate, ct);

            await RollUpLeagueAsync(leagueSnap.Reference, league, current, lines, lastStatDate, commitScore, dryRun, ct);
            Console.WriteLine();
        }

        Console.WriteLine($"=== reads={_reads}  writes={_writes}{(dryRun ? " (suppressed)" : "")} ===");
        if (_reads > 10_000)
            Console.Error.WriteLine("WARNING: over 10k reads. Something is scanning per player again.");
        return 0;
    }

    private async Task<IReadOnlyList<Period>> LoadPeriodsAsync(string season, CancellationToken ct)
    {
        var snap = await Read(db.Collection("periods").WhereEqualTo("season", season).OrderBy("index").GetSnapshotAsync(ct));
        return [.. snap.Documents.Select(d => d.ConvertTo<Period>())];
    }

    /// <summary>
    /// THE query. One date range over the current week returns every player's
    /// lines; season and game type are filtered in memory so no composite
    /// index is needed and the range stays on a single auto-indexed field.
    /// </summary>
    private async Task<ILookup<long, PlayerGameStats>> LoadWeekLinesAsync(
        string season, Period period, string lastStatDate, CancellationToken ct)
    {
        var to = string.CompareOrdinal(period.EndDate, lastStatDate) <= 0 ? period.EndDate : lastStatDate;
        var snap = await Read(db.Collection("playerGameStats")
            .WhereGreaterThanOrEqualTo("date", period.StartDate)
            .WhereLessThanOrEqualTo("date", to)
            .GetSnapshotAsync(ct));

        var lines = snap.Documents
            .Select(d => d.ConvertTo<PlayerGameStats>())
            .Where(l => l.Season == season && l.GameType == 2)
            .ToList();
        Console.WriteLine($"  Fetched {snap.Count} game lines for {period.StartDate}..{to} ({lines.Count} in season/regular).");
        return lines.ToLookup(l => l.PlayerId);
    }

    private async Task RollUpLeagueAsync(
        DocumentReference leagueDoc, League league, Period period,
        ILookup<long, PlayerGameStats> lines, string lastStatDate,
        bool commitScore, bool dryRun, CancellationToken ct)
    {
        var slots = LineupRules.SlotsFrom(league.RuleConfig);
        var pointValues = LegacyPointValues(league.RuleConfig);

        var spots = await ReadSpots(RosterSpots.RelevantToAsync(leagueDoc, period.StartDate, ct));
        var byTeam = spots.GroupBy(s => s.Spot.TeamUsername).ToDictionary(g => g.Key, g => g.ToList());

        var teamsSnap = await Read(leagueDoc.Collection("teams").GetSnapshotAsync(ct));
        var previous = await LoadLineupsAsync(leagueDoc, period.Index - 1, ct);

        foreach (var teamDoc in teamsSnap.Documents)
        {
            var team = teamDoc.ConvertTo<Team>();
            var teamSpots = byTeam.GetValueOrDefault(team.OwnerUsername, []);
            if (teamSpots.Count == 0) continue;

            var lineupRef = leagueDoc.Collection("lineups").Document(Lineup.DocId(team.OwnerUsername, period.Index));
            var lineupSnap = await Read(lineupRef.GetSnapshotAsync(ct));
            var lineup = lineupSnap.Exists ? lineupSnap.ConvertTo<Lineup>() : null;

            var candidates = teamSpots.Select(s => new LineupCandidate(
                SpotId: s.Ref.Id,
                PlayerId: s.Spot.PlayerId,
                PositionGroup: s.Spot.PositionGroup,
                SeasonPointsToDate: s.Spot.ActivePoints,
                ActivatedUtc: s.Spot.OpenedUtc.ToDateTime())).ToList();

            // No lineup for this week yet: carry last week's choices forward and
            // top them up, so a GM who forgot still fields a full team.
            var active = lineup is not null
                ? LineupRules.LegalActiveSet(candidates, lineup.ActiveSpotIds, slots)
                : LineupRules.CarryForward(candidates, previous.GetValueOrDefault(team.OwnerUsername, []), slots);

            var results = new Dictionary<string, LineupResult>();
            double activePoints = 0, benchPoints = 0;
            foreach (var (spotRef, spot) in teamSpots)
            {
                var window = StatWindow.Intersect(period.StartDate, period.EndDate, spot.StartDate, spot.EndDate, lastStatDate);
                if (window is null) continue;

                var stats = StatWindow.Sum(lines[spot.PlayerId], league.Season, window.Value.From, window.Value.To);
                var points = stats.Score(pointValues);
                results[spotRef.Id] = new LineupResult
                {
                    PlayerId = spot.PlayerId,
                    PositionGroup = spot.PositionGroup,
                    Points = points,
                    GamesPlayed = stats[StatKeys.GamesPlayed],
                    Stats = stats.ToMap().ToDictionary(kv => kv.Key, kv => (object)kv.Value),
                    FromDate = window.Value.From,
                    ToDate = window.Value.To,
                };
                if (active.Contains(spotRef.Id)) activePoints += points; else benchPoints += points;
            }

            var live = PeriodScoring.LiveScore(team.FinalizedScore, activePoints);
            Console.WriteLine($"    {team.OwnerUsername,-12} active {activePoints,7:0.#}  bench {benchPoints,6:0.#}  "
                + $"banked {team.FinalizedScore,7:0.#}  live {live,7:0.#}   (legacy score {team.Score:0.#})");

            if (dryRun) continue;

            // The job owns results/points; the GM owns activeSpotIds/setBy.
            // Writing only our own fields is what keeps a lineup submitted
            // mid-run from being clobbered -- a read-then-Set would lose any
            // change made between the read above and this write, and Firestore
            // does not conflict updates touching different fields.
            var jobFields = new Dictionary<string, object>
            {
                ["results"] = results,
                ["activePoints"] = activePoints,
                ["benchPoints"] = benchPoints,
                ["scoredUtc"] = Timestamp.GetCurrentTimestamp(),
            };
            if (lineup is null)
            {
                // No lineup for this week yet: creating it is ours to do, and
                // the carried-forward set is stamped "auto" so the UI can say so.
                jobFields["teamUsername"] = team.OwnerUsername;
                jobFields["periodIndex"] = period.Index;
                jobFields["season"] = league.Season;
                jobFields["activeSpotIds"] = active.ToList();
                jobFields["setBy"] = LineupSetBy.Auto;
                await Write(lineupRef.SetAsync(jobFields, cancellationToken: ct));
            }
            else
            {
                await Write(lineupRef.UpdateAsync(jobFields, cancellationToken: ct));
            }

            var teamFields = new Dictionary<string, object>
            {
                ["periodPoints"] = activePoints,
                ["benchScore"] = benchPoints,
                ["currentPeriodIndex"] = period.Index,
                [$"periodScores.{period.Index}"] = activePoints,
                ["scoreUpdatedUtc"] = Timestamp.GetCurrentTimestamp(),
            };
            // Shadow mode leaves `score` alone so the live app keeps showing the
            // legacy number until this has been watched for a few nights.
            if (commitScore) teamFields["score"] = live;
            await Write(teamDoc.Reference.UpdateAsync(teamFields, cancellationToken: ct));

            await RollUpSpotsAsync(teamSpots, results, active, ct);
        }
    }

    /// <summary>
    /// Spot rollups, written absolutely (finalized + current period) rather
    /// than accumulated, and skipped entirely when nothing moved — most players
    /// have no game on most nights.
    /// </summary>
    private async Task RollUpSpotsAsync(
        List<(DocumentReference Ref, RosterSpot Spot)> teamSpots,
        Dictionary<string, LineupResult> results,
        IReadOnlySet<string> active,
        CancellationToken ct)
    {
        var batch = db.StartBatch();
        var queued = 0;
        foreach (var (spotRef, spot) in teamSpots)
        {
            var result = results.GetValueOrDefault(spotRef.Id);
            var points = result?.Points ?? 0;
            var games = result?.GamesPlayed ?? 0;
            var isActive = active.Contains(spotRef.Id);

            var activeTotal = spot.FinalizedActivePoints + (isActive ? points : 0);
            var benchTotal = spot.FinalizedBenchPoints + (isActive ? 0 : points);
            // Compare against the values actually about to be written -- a
            // benched spot keeps its stored games played, so comparing that
            // against this week's game count would mark it dirty every night.
            var activeGames = isActive ? games : spot.ActiveGamesPlayed;
            if (!PeriodScoring.NeedsWrite(spot.ActivePoints, spot.ActiveGamesPlayed, activeTotal, activeGames)
                && spot.BenchPoints == benchTotal) continue;

            batch.Update(spotRef, new Dictionary<string, object>
            {
                ["activePoints"] = activeTotal,
                ["benchPoints"] = benchTotal,
                ["activeGamesPlayed"] = activeGames,
                ["rollupUpdatedUtc"] = Timestamp.GetCurrentTimestamp(),
            });
            if (++queued < BatchLimit) continue;

            await Write(batch.CommitAsync(ct));
            batch = db.StartBatch();
            queued = 0;
        }
        if (queued > 0) await Write(batch.CommitAsync(ct));
    }

    private async Task<Dictionary<string, List<string>>> LoadLineupsAsync(
        DocumentReference leagueDoc, int periodIndex, CancellationToken ct)
    {
        if (periodIndex < 1) return [];
        var snap = await Read(leagueDoc.Collection("lineups").WhereEqualTo("periodIndex", periodIndex).GetSnapshotAsync(ct));
        return snap.Documents
            .Select(d => d.ConvertTo<Lineup>())
            .ToDictionary(l => l.TeamUsername, l => l.ActiveSpotIds);
    }

    /// <summary>
    /// The league's scale as a StatLine map. Reads the legacy fixed PointValues
    /// for now; when RuleConfig moves to a keyed map this becomes a passthrough.
    /// </summary>
    private static Dictionary<string, double> LegacyPointValues(RuleConfig config) => new()
    {
        [StatKeys.Goals] = config.PointValues.Goal,
        [StatKeys.Assists] = config.PointValues.Assist,
        [StatKeys.Wins] = config.PointValues.GoalieWin,
        [StatKeys.OtLosses] = config.PointValues.GoalieOtLoss,
        [StatKeys.Shutouts] = config.PointValues.Shutout,
    };

    // --- read/write counting, so the cost claim is measured rather than assumed ---

    private async Task<QuerySnapshot> Read(Task<QuerySnapshot> task)
    {
        var snap = await task;
        _reads += Math.Max(snap.Count, 1);
        return snap;
    }

    private async Task<DocumentSnapshot> Read(Task<DocumentSnapshot> task)
    {
        _reads++;
        return await task;
    }

    private async Task<DocumentSnapshot> ReadDoc(Task<DocumentSnapshot> task) => await Read(task);

    private async Task<List<(DocumentReference Ref, RosterSpot Spot)>> ReadSpots(
        Task<List<(DocumentReference Ref, RosterSpot Spot)>> task)
    {
        var spots = await task;
        _reads += Math.Max(spots.Count, 1);
        return spots;
    }

    private async Task Write(Task task)
    {
        await task;
        _writes++;
    }
}
