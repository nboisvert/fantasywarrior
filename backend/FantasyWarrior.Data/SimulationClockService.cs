using Microsoft.EntityFrameworkCore;
using PoolClock = FantasyWarrior.Core.Time.PoolClock;
using SimulationState = FantasyWarrior.Data.Entities.SimulationState;

namespace FantasyWarrior.Data;

/// <summary>
/// Resolves "what time is it" — the simulated instant when a season replay is
/// running, the real one otherwise.
///
/// <c>PoolClock</c> deliberately stays pure and storage-free; this is the only
/// piece that knows a simulation can exist. Everything that used to read
/// <c>DateTimeOffset.UtcNow</c> asks this instead, which is what makes the
/// whole app believe it is the simulated day rather than only the scoring job.
/// </summary>
public sealed class SimulationClockService(FantasyWarriorDbContext db, TimeSpan? ttl = null)
{
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromSeconds(15);
    private SimulationState? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    /// <summary>The cursor, or null when no replay is running.</summary>
    public async Task<SimulationState?> StateAsync(CancellationToken ct = default)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < _ttl) return _cached;

        var row = await db.SimulationState.AsNoTracking().FirstOrDefaultAsync(ct);
        _cached = row is { Enabled: true } ? row : null;
        _cachedAt = DateTimeOffset.UtcNow;
        return _cached;
    }

    public async Task<DateTimeOffset> NowAsync(CancellationToken ct = default) =>
        await StateAsync(ct) is { } state ? FromAsOfDate(state.AsOfDate) : DateTimeOffset.UtcNow;

    public async Task<DateOnly> TodayEtAsync(CancellationToken ct = default) =>
        PoolClock.TodayEt(await NowAsync(ct));

    public async Task<DateOnly> LastStatDateAsync(CancellationToken ct = default) =>
        PoolClock.LastStatDate(await NowAsync(ct));

    /// <summary>
    /// Pure: the instant that makes <paramref name="asOfDate"/> the last known
    /// game day. Noon UTC on the following day sits safely inside that Eastern
    /// day whether or not daylight saving is in effect, so the derived date is
    /// never off by one — and it reproduces the real-world relationship
    /// (lastStatDate = today - 1) exactly, which is why no scoring code needs a
    /// special case for simulation.
    /// </summary>
    public static DateTimeOffset FromAsOfDate(DateOnly asOfDate) =>
        new(asOfDate.AddDays(1).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    /// <summary>Moves the cursor. Only the test-mode jobs call this.</summary>
    public async Task SetAsync(DateOnly asOfDate, string season, CancellationToken ct = default)
    {
        var row = await db.SimulationState.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            db.SimulationState.Add(new SimulationState
            {
                SimulationStateId = 1,
                AsOfDate = asOfDate,
                Season = season,
                Enabled = true,
                UpdatedUtc = DateTime.UtcNow,
            });
        }
        else
        {
            row.AsOfDate = asOfDate;
            row.Season = season;
            row.Enabled = true;
            row.UpdatedUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        _cached = null;
    }

    /// <summary>Returns every reader to the real clock, keeping the cursor.</summary>
    public async Task DisableAsync(CancellationToken ct = default)
    {
        var row = await db.SimulationState.FirstOrDefaultAsync(ct);
        if (row is null) return;
        row.Enabled = false;
        row.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        _cached = null;
    }
}
