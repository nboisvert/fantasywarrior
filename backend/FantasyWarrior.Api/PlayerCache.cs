using FantasyWarrior.Core.Players;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Api;

/// <summary>
/// In-memory copy of the `players` collection for name search
/// (Firestore has no full-text search; ~1400 docs fit fine in memory).
/// </summary>
public sealed class PlayerCache(FirestoreDb db)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<Entry> _entries = [];
    private DateTime _loadedUtc = DateTime.MinValue;

    public async Task<IReadOnlyList<Player>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        var entries = await GetEntriesAsync(ct);
        var q = NameNormalizer.Normalize(query);
        if (q.Length == 0)
            return [];
        return entries
            .Where(e => e.NormalizedName.Contains(q))
            .OrderBy(e => e.NormalizedName.StartsWith(q) ? 0 : 1)
            .ThenBy(e => e.NormalizedName)
            .Take(limit)
            .Select(e => e.Player)
            .ToList();
    }

    public async Task<Dictionary<long, Player>> GetByIdsAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        var entries = await GetEntriesAsync(ct);
        var wanted = ids.ToHashSet();
        return entries.Where(e => wanted.Contains(e.Player.NhlId)).ToDictionary(e => e.Player.NhlId, e => e.Player);
    }

    private async Task<List<Entry>> GetEntriesAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _loadedUtc < Ttl)
            return _entries;
        await _lock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _loadedUtc >= Ttl)
            {
                var snapshot = await db.Collection("players").GetSnapshotAsync(ct);
                _entries = snapshot.Documents
                    .Select(d => d.ConvertTo<Player>())
                    .Select(p => new Entry(p, NameNormalizer.Normalize($"{p.FirstName} {p.LastName}")))
                    .ToList();
                _loadedUtc = DateTime.UtcNow;
            }
            return _entries;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record Entry(Player Player, string NormalizedName);
}
