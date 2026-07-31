using FantasyWarrior.Core.Time;

namespace FantasyWarrior.Core.Tests.Time;

/// <summary>
/// These pin the one thing a week-based scoring model cannot get wrong: which
/// game day a given instant belongs to. An off-by-one here does not shift a
/// row, it shifts a whole scoring period.
/// </summary>
public class PoolClockTests
{
    private static DateTimeOffset Utc(int y, int m, int d, int h, int min) =>
        new(y, m, d, h, min, 0, TimeSpan.Zero);

    [Fact]
    public void TodayEt_during_EST_is_UTC_minus_five()
    {
        // 2026-01-15 04:59 UTC is still 2026-01-14 23:59 in Eastern Standard Time.
        Assert.Equal(new DateOnly(2026, 1, 14), PoolClock.TodayEt(Utc(2026, 1, 15, 4, 59)));
        Assert.Equal(new DateOnly(2026, 1, 15), PoolClock.TodayEt(Utc(2026, 1, 15, 5, 0)));
    }

    [Fact]
    public void TodayEt_during_EDT_is_UTC_minus_four()
    {
        // 2026-07-15 03:59 UTC is still 2026-07-14 23:59 in Eastern Daylight Time.
        Assert.Equal(new DateOnly(2026, 7, 14), PoolClock.TodayEt(Utc(2026, 7, 15, 3, 59)));
        Assert.Equal(new DateOnly(2026, 7, 15), PoolClock.TodayEt(Utc(2026, 7, 15, 4, 0)));
    }

    [Fact]
    public void TodayEt_handles_the_spring_forward_switch()
    {
        // DST starts 2026-03-08 02:00 ET. The day itself must not be skipped or doubled.
        Assert.Equal(new DateOnly(2026, 3, 7), PoolClock.TodayEt(Utc(2026, 3, 8, 4, 59)));  // EST, still Mar 7
        Assert.Equal(new DateOnly(2026, 3, 8), PoolClock.TodayEt(Utc(2026, 3, 8, 5, 0)));   // EST, now Mar 8
        Assert.Equal(new DateOnly(2026, 3, 8), PoolClock.TodayEt(Utc(2026, 3, 8, 7, 0)));   // EDT, 03:00 Mar 8
        Assert.Equal(new DateOnly(2026, 3, 9), PoolClock.TodayEt(Utc(2026, 3, 9, 4, 0)));   // EDT, 00:00 Mar 9
    }

    [Fact]
    public void TodayEt_handles_the_fall_back_switch()
    {
        // DST ends 2026-11-01 02:00 ET; 01:30 ET happens twice. Both are Nov 1.
        Assert.Equal(new DateOnly(2026, 11, 1), PoolClock.TodayEt(Utc(2026, 11, 1, 5, 30)));  // EDT 01:30
        Assert.Equal(new DateOnly(2026, 11, 1), PoolClock.TodayEt(Utc(2026, 11, 1, 6, 30)));  // EST 01:30
        Assert.Equal(new DateOnly(2026, 10, 31), PoolClock.TodayEt(Utc(2026, 11, 1, 3, 59))); // EDT 23:59 Oct 31
    }

    [Fact]
    public void LastStatDate_is_the_day_before_the_ET_game_day()
    {
        // The nightly cron fires at 09:30 UTC — comfortably inside the ET day.
        Assert.Equal(new DateOnly(2026, 1, 14), PoolClock.LastStatDate(Utc(2026, 1, 15, 9, 30)));
    }

    [Fact]
    public void LastStatDate_before_ET_midnight_still_points_at_the_previous_game_day()
    {
        // This is the latent bug the old `DateTime.UtcNow.Date.AddDays(-1)` had:
        // a manual re-run at 02:00 UTC on Jan 15 is still Jan 14 in ET, so the
        // last fully-synced game day is Jan 13 — not Jan 14.
        Assert.Equal(new DateOnly(2026, 1, 13), PoolClock.LastStatDate(Utc(2026, 1, 15, 2, 0)));
    }

    [Fact]
    public void Iso_uses_the_Firestore_date_field_format()
    {
        Assert.Equal("2026-03-08", PoolClock.Iso(new DateOnly(2026, 3, 8)));
        Assert.Equal("2026-01-15", PoolClock.TodayEtIso(Utc(2026, 1, 15, 9, 30)));
        Assert.Equal("2026-01-14", PoolClock.LastStatDateIso(Utc(2026, 1, 15, 9, 30)));
    }
}
