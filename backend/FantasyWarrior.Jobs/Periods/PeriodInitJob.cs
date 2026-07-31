using FantasyWarrior.Core.Periods;
using FantasyWarrior.Core.Stats;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Periods;

/// <summary>
/// Generates a season's weekly scoring calendar into the global `periods`
/// collection.
///
/// Season boundaries are derived from the `games` collection already in
/// Firestore rather than fetched from the NHL API or hardcoded — the schedule
/// is data we own, and deriving it means the calendar can never disagree with
/// the games it scores.
///
/// **Append-only.** Existing period documents are never rewritten: points are
/// banked per period, so moving a boundary after the fact would silently
/// restate history. Re-running only fills in weeks that don't exist yet.
/// </summary>
public sealed class PeriodInitJob(FirestoreDb db)
{
    private const int RegularSeason = 2;

    public async Task<int> RunAsync(string season, bool dryRun, CancellationToken ct = default)
    {
        var gamesSnap = await db.Collection("games")
            .WhereEqualTo("season", season)
            .WhereEqualTo("gameType", RegularSeason)
            .GetSnapshotAsync(ct);

        if (gamesSnap.Count == 0)
        {
            Console.Error.WriteLine($"No regular-season games found for {season}. Run stats-sync first.");
            return 1;
        }

        var games = gamesSnap.Documents.Select(d => d.ConvertTo<Game>()).ToList();
        var dates = games.Select(g => g.Date).Where(d => d.Length == 10).ToList();
        var first = DateOnly.Parse(dates.Min()!);
        var last = DateOnly.Parse(dates.Max()!);

        var spans = PeriodCalendar.Generate(first, last);
        var perDay = games.GroupBy(g => g.Date).ToDictionary(g => g.Key, g => g.Count());

        Console.WriteLine($"=== period-init {season}{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{gamesSnap.Count} regular-season games, {first:yyyy-MM-dd} -> {last:yyyy-MM-dd}");
        Console.WriteLine($"{spans.Count} weeks, anchored on {spans[0].Start:yyyy-MM-dd} (Monday)\n");

        var existing = (await db.Collection("periods").WhereEqualTo("season", season).GetSnapshotAsync(ct))
            .Documents.Select(d => d.Id).ToHashSet();

        var now = Timestamp.GetCurrentTimestamp();
        var created = 0;
        foreach (var span in spans)
        {
            var id = PeriodId.For(season, span.Index);
            var gameCount = Enumerable.Range(0, 7)
                .Sum(i => perDay.GetValueOrDefault(span.Start.AddDays(i).ToString("yyyy-MM-dd")));

            var known = existing.Contains(id);
            Console.WriteLine($"  {id}  {span.StartIso} -> {span.EndIso}  {gameCount,3} games"
                + (gameCount == 0 ? "   (break week)" : "")
                + (known ? "   [exists, untouched]" : ""));

            if (known || dryRun) continue;

            await db.Collection("periods").Document(id).SetAsync(new Period
            {
                Season = season,
                Index = span.Index,
                StartDate = span.StartIso,
                EndDate = span.EndIso,
                LockUtc = Timestamp.FromDateTime(PeriodCalendar.LockUtcFor(span.Start)),
                GameCount = gameCount,
                FinalizedUtc = null,
                CreatedUtc = now,
            }, cancellationToken: ct);
            created++;
        }

        Console.WriteLine(dryRun
            ? $"\nDry run: nothing written ({spans.Count - existing.Count} would be created)."
            : $"\nCreated {created} period(s); {existing.Count} already existed and were left untouched.");
        return 0;
    }
}
