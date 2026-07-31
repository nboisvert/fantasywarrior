using Google.Cloud.Firestore;
using Grpc.Core;

namespace FantasyWarrior.Jobs.Ops;

/// <summary>
/// Executes every composite-index-requiring query shape the app uses, with
/// <c>Limit(1)</c>, and reports which ones are missing an index.
///
/// This job exists because the Firestore **emulator does not enforce composite
/// indexes at all**. A query can pass every local test and every emulator run,
/// then fail in production with FAILED_PRECONDITION the first time it executes.
/// Point this at real Firestore (GOOGLE_APPLICATION_CREDENTIALS +
/// FIRESTORE_PROJECT_ID, no FIRESTORE_EMULATOR_HOST) — running it against the
/// emulator is worse than not running it, because everything passes.
///
/// Firestore embeds a ready-made console creation URL in the error message, so
/// the fix is a click. Whatever you create, mirror it into
/// <c>firestore.indexes.json</c> so the next environment gets it for free.
/// </summary>
public sealed class CheckIndexesJob(FirestoreDb db)
{
    /// <summary>
    /// Every query shape the app issues that could need a composite index.
    /// A league with no documents still proves the index exists — Firestore
    /// validates the index before it looks at any data.
    /// </summary>
    private IEnumerable<(string Name, Query Query)> Shapes(string leagueId)
    {
        var league = db.Collection("leagues").Document(leagueId);

        yield return ("periods(season, index) — season calendar, ordered",
            db.Collection("periods").WhereEqualTo("season", "20262027").OrderBy("index"));

        yield return ("playerGameStats(date range) — THE nightly hot query (expects NO composite index)",
            db.Collection("playerGameStats")
                .WhereGreaterThanOrEqualTo("date", "2000-01-01").WhereLessThanOrEqualTo("date", "2000-01-08"));

        yield return ("rosterSpots(teamUsername, endDate) — open spots for one team",
            league.Collection("rosterSpots")
                .WhereEqualTo("teamUsername", "probe").WhereEqualTo("endDate", null));

        yield return ("rosterSpots(teamUsername, endDate>=) — spots closed mid-period",
            league.Collection("rosterSpots")
                .WhereEqualTo("teamUsername", "probe").WhereGreaterThanOrEqualTo("endDate", "2000-01-01"));

        yield return ("rosterSpots(playerId, endDate) — a player's open spot",
            league.Collection("rosterSpots")
                .WhereEqualTo("playerId", 0L).WhereEqualTo("endDate", null));

        yield return ("lineups(periodIndex) — every team's lineup for one period",
            league.Collection("lineups").WhereEqualTo("periodIndex", 1));

        yield return ("assignments(teamUsername, to) — LEGACY, drop after the RosterSpot cutover",
            league.Collection("assignments")
                .WhereEqualTo("teamUsername", "probe").WhereEqualTo("to", null));
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        if (Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") is { } emulator)
        {
            Console.Error.WriteLine(
                $"REFUSING TO RUN: FIRESTORE_EMULATOR_HOST is set ({emulator}). The emulator ignores " +
                "composite indexes, so every shape would pass and tell you nothing. Point this at real Firestore.");
            return 1;
        }

        // Any league id works — the index check happens before data is read.
        var leagues = await db.Collection("leagues").Limit(1).GetSnapshotAsync(ct);
        var leagueId = leagues.Documents.FirstOrDefault()?.Id ?? "index-probe-nonexistent-league";
        Console.WriteLine($"check-indexes: probing against league '{leagueId}'\n");

        var missing = 0;
        foreach (var (name, query) in Shapes(leagueId))
            missing += await ProbeAsync(name, query, ct);

        Console.WriteLine();
        if (missing == 0)
        {
            Console.WriteLine("check-indexes: all query shapes are served.");
            return 0;
        }

        Console.Error.WriteLine(
            $"check-indexes: {missing} query shape(s) MISSING an index. Create them via the URLs above, " +
            "then mirror them into firestore.indexes.json.");
        return 1;
    }

    private static async Task<int> ProbeAsync(string name, Query query, CancellationToken ct)
    {
        try
        {
            await query.Limit(1).GetSnapshotAsync(ct);
            Console.WriteLine($"  OK      {name}");
            return 0;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition)
        {
            Console.Error.WriteLine($"  MISSING {name}");
            Console.Error.WriteLine($"          {ex.Status.Detail}");
            return 1;
        }
    }
}
