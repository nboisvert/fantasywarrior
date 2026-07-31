using Google.Cloud.Firestore;
using Google.Cloud.Firestore.Admin.V1;
using Grpc.Core;
// System.Index (the ^1 indexer type) collides with the Firestore Admin one.
using FsIndex = Google.Cloud.Firestore.Admin.V1.Index;

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

    /// <summary>
    /// The composite indexes this app needs, mirroring firestore.indexes.json.
    /// Single-field and range-only queries are auto-indexed by Firestore and
    /// are deliberately absent.
    /// </summary>
    private static readonly (string Collection, (string Field, bool Descending)[] Fields)[] Required =
    [
        ("rosterSpots", [("teamUsername", false), ("endDate", false)]),
        ("rosterSpots", [("playerId", false), ("endDate", false)]),
        ("periods", [("season", false), ("index", false)]),
        ("assignments", [("teamUsername", false), ("to", false)]),
    ];

    public async Task<int> RunAsync(bool create, CancellationToken ct = default)
    {
        if (Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") is { } emulator)
        {
            Console.Error.WriteLine(
                $"REFUSING TO RUN: FIRESTORE_EMULATOR_HOST is set ({emulator}). The emulator ignores " +
                "composite indexes, so every shape would pass and tell you nothing. Point this at real Firestore.");
            return 1;
        }

        if (create) await CreateMissingAsync(ct);

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
            $"check-indexes: {missing} query shape(s) MISSING an index. Re-run with --create, "
            + "or use the URLs above. Mirror anything new into firestore.indexes.json.");
        return 1;
    }

    /// <summary>
    /// Creates the required composite indexes through the Firestore Admin API,
    /// using the same service-account credentials as every other job. This
    /// exists because neither the Firebase nor the gcloud CLI is installed on
    /// the dev box, and hand-creating indexes from console error links leaves
    /// no record of what exists.
    ///
    /// Index builds are asynchronous — Firestore returns immediately and the
    /// index becomes queryable minutes later, so the probe that follows may
    /// still report it missing on the first run.
    /// </summary>
    private async Task CreateMissingAsync(CancellationToken ct)
    {
        var admin = await new FirestoreAdminClientBuilder().BuildAsync(ct);
        Console.WriteLine("check-indexes --create: ensuring composite indexes exist\n");
        var permissionDenied = false;

        foreach (var (collection, fields) in Required)
        {
            var parent = CollectionGroupName.FromProjectDatabaseCollection(db.ProjectId, "(default)", collection);
            var label = $"{collection}({string.Join(", ", fields.Select(f => f.Field))})";
            var index = new FsIndex { QueryScope = FsIndex.Types.QueryScope.Collection };
            foreach (var (field, descending) in fields)
                index.Fields.Add(new FsIndex.Types.IndexField
                {
                    FieldPath = field,
                    Order = descending
                        ? FsIndex.Types.IndexField.Types.Order.Descending
                        : FsIndex.Types.IndexField.Types.Order.Ascending,
                });

            try
            {
                await admin.CreateIndexAsync(parent, index, ct);
                Console.WriteLine($"  CREATED  {label}  (building, usable in a few minutes)");
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
            {
                Console.WriteLine($"  exists   {label}");
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                Console.Error.WriteLine($"  DENIED   {label}");
                permissionDenied = true;
            }
            catch (RpcException ex)
            {
                Console.Error.WriteLine($"  FAILED   {label}: {ex.Status.Detail}");
            }
        }

        if (permissionDenied)
            Console.Error.WriteLine(
                "\n  The service account can read and write data but not create indexes.\n"
                + "  Grant it \"Cloud Datastore Index Admin\" (roles/datastore.indexAdmin) in\n"
                + "  IAM for the project, then re-run. Until then, create them from the\n"
                + "  console links the probe below prints.");
        Console.WriteLine();
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
