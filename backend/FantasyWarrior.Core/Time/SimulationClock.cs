using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Time;

/// <summary>
/// The simulation cursor: <c>simulation/clock</c>, a single global document.
///
/// Nothing else stores it. One writer (the test-mode jobs), one document, and
/// every reader — the nightly pipeline, the local API, the deployed API — sees
/// the same value. There is nothing to keep in sync and nothing to parse.
/// </summary>
[FirestoreData]
public sealed class SimulationState
{
    /// <summary>
    /// The last game day whose results are known, "YYYY-MM-DD" ET.
    ///
    /// The simulated instant is derived as the *next* day at noon UTC, which
    /// makes <see cref="PoolClock.TodayEt"/> = asOfDate + 1 and
    /// <see cref="PoolClock.LastStatDate"/> = asOfDate — exactly the real-world
    /// relationship, so no scoring code needs a special case for simulation.
    /// </summary>
    [FirestoreProperty("asOfDate")]
    public string AsOfDate { get; set; } = "";

    [FirestoreProperty("season")]
    public string Season { get; set; } = "";

    /// <summary>Lets the simulation be paused without deleting the cursor.</summary>
    [FirestoreProperty("enabled")]
    public bool Enabled { get; set; }

    [FirestoreProperty("updatedUtc")]
    public Timestamp UpdatedUtc { get; set; }
}

/// <summary>
/// Resolves "what time is it" — the simulated instant when a simulation is
/// running, the real one otherwise.
///
/// <see cref="PoolClock"/> deliberately stays pure and Firestore-free; this is
/// the only piece that knows a simulation can exist. Every caller that used to
/// pass <c>DateTimeOffset.UtcNow</c> into PoolClock now passes
/// <see cref="NowAsync"/> instead.
///
/// The API resolves this per request behind a short TTL cache (same reasoning
/// as <c>PlayerCache</c>): a Firestore read on every request would be wasteful,
/// and a few seconds of staleness cannot matter when the cursor only moves when
/// someone runs a job.
/// </summary>
public sealed class SimulationClock(FirestoreDb db, TimeSpan? ttl = null)
{
    public const string CollectionId = "simulation";
    public const string DocumentId = "clock";

    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromSeconds(15);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SimulationState? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public static DocumentReference Document(FirestoreDb db) =>
        db.Collection(CollectionId).Document(DocumentId);

    /// <summary>
    /// Pure: the instant that makes <paramref name="asOfDate"/> the last known
    /// game day. Noon UTC on the following day sits safely inside that ET day
    /// whether or not daylight saving is in effect, so the derived ET date is
    /// never off by one.
    /// </summary>
    public static DateTimeOffset FromAsOfDate(string asOfDate) =>
        FromAsOfDate(DateOnly.Parse(asOfDate));

    public static DateTimeOffset FromAsOfDate(DateOnly asOfDate) =>
        new(asOfDate.AddDays(1).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    /// <summary>The current simulation cursor, or null when none is running.</summary>
    public async Task<SimulationState?> StateAsync(CancellationToken ct = default)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < _ttl) return _cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < _ttl) return _cached;

            var snap = await Document(db).GetSnapshotAsync(ct);
            var state = snap.Exists ? snap.ConvertTo<SimulationState>() : null;
            _cached = state is { Enabled: true, AsOfDate.Length: 10 } ? state : null;
            _cachedAt = DateTimeOffset.UtcNow;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The simulated instant, or the real one when no simulation is running.</summary>
    public async Task<DateTimeOffset> NowAsync(CancellationToken ct = default) =>
        await StateAsync(ct) is { } state ? FromAsOfDate(state.AsOfDate) : DateTimeOffset.UtcNow;

    public async Task<string> TodayEtAsync(CancellationToken ct = default) =>
        PoolClock.TodayEtIso(await NowAsync(ct));

    public async Task<string> LastStatDateAsync(CancellationToken ct = default) =>
        PoolClock.LastStatDateIso(await NowAsync(ct));

    /// <summary>Moves the cursor. Only the test-mode jobs call this.</summary>
    public async Task SetAsync(string asOfDate, string season, CancellationToken ct = default)
    {
        await Document(db).SetAsync(new SimulationState
        {
            AsOfDate = asOfDate,
            Season = season,
            Enabled = true,
            UpdatedUtc = Timestamp.GetCurrentTimestamp(),
        }, cancellationToken: ct);
        _cached = null;
    }

    /// <summary>Stops the simulation and returns every reader to the real clock.</summary>
    public async Task DisableAsync(CancellationToken ct = default)
    {
        await Document(db).SetAsync(new Dictionary<string, object> { ["enabled"] = false }, SetOptions.MergeAll, ct);
        _cached = null;
    }
}
