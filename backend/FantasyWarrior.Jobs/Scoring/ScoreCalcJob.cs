using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Players;
using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Scoring;

/// <summary>
/// Nightly score recalculation. For each league: fetch season raw totals of
/// every rostered player, run the top-X engine per team, add the adjustment
/// ledger, persist computed fields on team docs. Stateless and idempotent.
///
/// Also (2026-07-23): writes a per-team `scoreLedger` audit entry for any
/// player whose counted-flag flipped or whose cumulative points changed
/// since the last run — the score itself was already correct before this
/// (each run recomputes top-X from scratch, so a player crossing the
/// top-X boundary already nets out correctly), this just makes that
/// movement explainable. And refreshes each open (`to == null`) roster
/// assignment's per-stint NHL/pool stats.
/// </summary>
public sealed class ScoreCalcJob(FirestoreDb db)
{
    public async Task RunAsync(string? onlyLeagueId = null, CancellationToken ct = default)
    {
        var leagues = onlyLeagueId is null
            ? (await db.Collection("leagues").GetSnapshotAsync(ct)).Documents.ToList()
            : [await db.Collection("leagues").Document(onlyLeagueId).GetSnapshotAsync(ct)];

        // Matches the convention used by stats-sync/daily-jobs.yml: the cron
        // runs after 9:30 UTC, once all of "yesterday UTC"'s games are final.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1).ToString("yyyy-MM-dd");

        foreach (var leagueSnap in leagues.Where(l => l.Exists))
        {
            var league = leagueSnap.ConvertTo<League>();
            var teamsSnap = await leagueSnap.Reference.Collection("teams").GetSnapshotAsync(ct);
            var teams = teamsSnap.Documents.Select(d => (Doc: d, Team: d.ConvertTo<Team>())).ToList();

            var allPlayerIds = teams.SelectMany(t => t.Team.PlayerIds).Distinct().ToList();
            var totals = await PlayerTotalsSource.FetchAsync(db, allPlayerIds, league.Season, ct);
            var playersById = await FetchPlayersAsync(allPlayerIds, ct);
            string PlayerName(long id) => playersById.TryGetValue(id, out var p) ? $"{p.FirstName} {p.LastName}" : "";
            string PlayerPosition(long id) => playersById.TryGetValue(id, out var p) ? p.Position : "C";

            Console.WriteLine($"League '{league.Name}' [{leagueSnap.Id}] season {league.Season}: {teams.Count} teams, {allPlayerIds.Count} players");

            foreach (var (doc, team) in teams)
            {
                var entries = team.PlayerIds
                    .Select(id => new RosterEntry(id, PlayerPosition(id), totals.GetValueOrDefault(id) ?? new PlayerRawTotals()))
                    .ToList();
                var result = ScoringEngine.TeamScore(entries, league.RuleConfig);

                var adjustments = await doc.Reference.Collection("adjustments").GetSnapshotAsync(ct);
                var adjustmentsTotal = adjustments.Documents.Sum(a => a.GetValue<double>("delta"));

                await WriteLedgerEntriesAsync(doc.Reference, team, result, PlayerName, PlayerPosition, today, ct);

                await doc.Reference.UpdateAsync(new Dictionary<string, object>
                {
                    ["score"] = result.RawTopX + adjustmentsTotal,
                    ["rawTopXScore"] = result.RawTopX,
                    ["adjustmentsTotal"] = adjustmentsTotal,
                    ["countedPlayerIds"] = result.CountedPlayerIds.ToList(),
                    ["playerPoints"] = result.PlayerPoints.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                    ["scoreUpdatedUtc"] = Timestamp.GetCurrentTimestamp(),
                }, cancellationToken: ct);

                Console.WriteLine($"  {team.Name} (@{team.OwnerUsername}): score {result.RawTopX + adjustmentsTotal:0.##} (raw {result.RawTopX:0.##}, adj {adjustmentsTotal:0.##}), counted {result.CountedPlayerIds.Count}/{team.PlayerIds.Count}");
            }

            await RefreshOpenAssignmentsAsync(leagueSnap.Reference, league, ct);
        }
    }

    /// <summary>
    /// Diffs this run's counted-flags/points against what was already on the
    /// team doc (i.e. last run's result) and writes one ledger entry per
    /// player whose flag flipped or whose points changed — not one row per
    /// rostered player every night.
    /// </summary>
    private async Task WriteLedgerEntriesAsync(
        DocumentReference teamDoc, Team team, TeamScoreResult result,
        Func<long, string> playerName, Func<long, string> playerPosition, string today, CancellationToken ct)
    {
        var countedBefore = team.CountedPlayerIds.ToHashSet();
        var countedAfter = result.CountedPlayerIds.ToHashSet();
        var ledgerCol = teamDoc.Collection("scoreLedger");
        var now = Timestamp.GetCurrentTimestamp();
        var batch = db.StartBatch();
        var writes = 0;

        foreach (var playerId in team.PlayerIds)
        {
            var before = team.PlayerPoints.GetValueOrDefault(playerId.ToString(), 0);
            var after = result.PlayerPoints.GetValueOrDefault(playerId, 0);
            var wasCounted = countedBefore.Contains(playerId);
            var isCounted = countedAfter.Contains(playerId);
            if (!ScoreLedgerLogic.ShouldRecord(before, after, wasCounted, isCounted))
                continue;

            batch.Set(ledgerCol.Document(), new ScoreLedgerEntry
            {
                Date = today,
                PlayerId = playerId,
                PlayerName = playerName(playerId),
                Position = playerPosition(playerId),
                FantasyPointsBefore = before,
                FantasyPointsAfter = after,
                CountedBefore = wasCounted,
                CountedAfter = isCounted,
                CreatedUtc = now,
            });
            writes++;
        }

        if (writes > 0)
        {
            await batch.CommitAsync(ct);
            Console.WriteLine($"    scoreLedger: {writes} entries for {team.Name}");
        }
    }

    /// <summary>
    /// Refreshes every open assignment's per-stint stats for this league.
    /// Closed assignments (to != null) are left untouched — frozen at
    /// whatever they were computed to be when they closed.
    /// </summary>
    private async Task RefreshOpenAssignmentsAsync(DocumentReference leagueRef, League league, CancellationToken ct)
    {
        var openSnap = await leagueRef.Collection("assignments").WhereEqualTo("to", null).GetSnapshotAsync(ct);
        if (openSnap.Count == 0)
            return;

        var pointValues = league.RuleConfig.PointValues;
        var now = Timestamp.GetCurrentTimestamp();
        var updated = 0;
        foreach (var chunk in openSnap.Documents.Chunk(8))
        {
            var tasks = chunk.Select(async assignmentDoc =>
            {
                var assignment = assignmentDoc.ConvertTo<Assignment>();
                var lines = await PlayerTotalsSource.FetchLinesAsync(db, assignment.PlayerId, ct);
                var totals = PlayerTotalsSource.AggregateRange(lines, league.Season, assignment.From, assignment.To);
                var fantasyPoints = ScoringEngine.PlayerPoints(totals, pointValues);
                return (assignmentDoc.Reference, totals, fantasyPoints);
            });

            var batch = db.StartBatch();
            foreach (var (reference, totals, fantasyPoints) in await Task.WhenAll(tasks))
            {
                batch.Update(reference, AssignmentStats.ToFieldMap(totals, fantasyPoints, now));
                updated++;
            }
            await batch.CommitAsync(ct);
        }
        Console.WriteLine($"  assignments: refreshed {updated} open stint(s)");
    }

    private async Task<Dictionary<long, Player>> FetchPlayersAsync(
        IReadOnlyCollection<long> playerIds, CancellationToken ct)
    {
        if (playerIds.Count == 0)
            return [];
        var refs = playerIds.Select(id => db.Collection("players").Document(id.ToString())).ToArray();
        var snaps = await db.GetAllSnapshotsAsync(refs, ct);
        return snaps
            .Where(s => s.Exists)
            .ToDictionary(s => long.Parse(s.Id), s => s.ConvertTo<Player>());
    }
}
