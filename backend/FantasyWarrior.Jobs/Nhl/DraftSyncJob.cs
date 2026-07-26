using FantasyWarrior.Core.Players;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Nhl;

/// <summary>
/// Backfills NHL entry draft details (round/overall pick/year/team) for
/// every player not yet checked. One HTTP call per player (the landing
/// endpoint is the only source), so this is its own job rather than folded
/// into the nightly player-sync — `draftChecked` lets re-runs (new players
/// joining the pool later) skip everyone already looked up.
/// </summary>
public sealed class DraftSyncJob(NhlApiClient nhl, FirestoreDb db)
{
    private const int FirestoreBatchLimit = 500;

    private static readonly SetOptions MergeDraftFields = SetOptions.MergeFields(
        "draftYear", "draftRound", "draftOverall", "draftTeamAbbrev", "draftChecked");

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var snap = await db.Collection("players").GetSnapshotAsync(ct);
        var pending = snap.Documents
            .Select(d => d.ConvertTo<Player>())
            .Where(p => !p.DraftChecked)
            .ToList();
        Console.WriteLine($"DraftSync: {pending.Count} of {snap.Count} players need checking");

        var checkedCount = 0;
        var draftedCount = 0;
        foreach (var chunk in pending.Chunk(FirestoreBatchLimit))
        {
            var batch = db.StartBatch();
            foreach (var player in chunk)
            {
                var landing = await nhl.GetPlayerLandingAsync(player.NhlId, ct);
                var draft = landing?.DraftDetails;
                if (draft?.Year is not null)
                    draftedCount++;

                batch.Set(db.Collection("players").Document(player.NhlId.ToString()), new Player
                {
                    DraftYear = draft?.Year,
                    DraftRound = draft?.Round,
                    DraftOverall = draft?.OverallPick,
                    DraftTeamAbbrev = draft?.TeamAbbrev,
                    DraftChecked = true,
                }, MergeDraftFields);
                checkedCount++;
            }
            await batch.CommitAsync(ct);
            Console.WriteLine($"  checked {checkedCount}/{pending.Count} ({draftedCount} drafted so far)");
        }

        Console.WriteLine($"DraftSync: checked {checkedCount} players, {draftedCount} had draft details.");
        return checkedCount;
    }
}
