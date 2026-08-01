using FantasyWarrior.Core.Time;

namespace FantasyWarrior.Core.Tests.Time;

/// <summary>
/// The simulation cursor has to satisfy exactly one property: feeding its
/// derived instant to PoolClock must reproduce the real-world relationship
/// (last known game day = asOfDate, "today" = the morning after). If it
/// doesn't, every scoring window in the simulation is off by a day.
/// </summary>
public class SimulationClockTests
{
    [Fact]
    public void AsOfDate_IsTheLastKnownGameDay()
    {
        var now = SimulationClock.FromAsOfDate("2025-11-23");

        Assert.Equal(new DateOnly(2025, 11, 23), PoolClock.LastStatDate(now));
        Assert.Equal(new DateOnly(2025, 11, 24), PoolClock.TodayEt(now));
    }

    [Fact]
    public void TheRelationshipHoldsOnTheSeasonEve()
    {
        // The cursor starts the day before period 1 so the first lineup is
        // still editable: nothing has been played, and "today" is the Monday
        // the week opens on.
        var now = SimulationClock.FromAsOfDate("2025-10-05");

        Assert.Equal(new DateOnly(2025, 10, 5), PoolClock.LastStatDate(now));
        Assert.Equal(new DateOnly(2025, 10, 6), PoolClock.TodayEt(now));
    }

    [Fact]
    public void NoonUtcKeepsTheDerivedDayCorrectUnderEasternStandardTime()
    {
        // January: ET is UTC-5. Noon UTC is 07:00 ET — same day, comfortably.
        var now = SimulationClock.FromAsOfDate("2026-01-14");

        Assert.Equal(new DateOnly(2026, 1, 15), PoolClock.TodayEt(now));
    }

    [Fact]
    public void NoonUtcKeepsTheDerivedDayCorrectUnderEasternDaylightTime()
    {
        // July: ET is UTC-4. Noon UTC is 08:00 ET — still the same day.
        var now = SimulationClock.FromAsOfDate("2026-07-14");

        Assert.Equal(new DateOnly(2026, 7, 15), PoolClock.TodayEt(now));
    }

    [Fact]
    public void TheDerivedDayIsCorrectAcrossBothDaylightSavingSwitches()
    {
        // Spring forward is 2026-03-08, fall back 2026-11-01. Landing the
        // instant at noon rather than midnight is what keeps these safe.
        Assert.Equal(new DateOnly(2026, 3, 8), PoolClock.TodayEt(SimulationClock.FromAsOfDate("2026-03-07")));
        Assert.Equal(new DateOnly(2026, 3, 9), PoolClock.TodayEt(SimulationClock.FromAsOfDate("2026-03-08")));
        Assert.Equal(new DateOnly(2026, 11, 1), PoolClock.TodayEt(SimulationClock.FromAsOfDate("2026-10-31")));
        Assert.Equal(new DateOnly(2026, 11, 2), PoolClock.TodayEt(SimulationClock.FromAsOfDate("2026-11-01")));
    }

    [Fact]
    public void EveryDayOfASeasonRoundTrips()
    {
        // Cheap exhaustive guard: any off-by-one in the derivation, at any
        // point in the season including both DST switches, fails here.
        for (var d = new DateOnly(2025, 10, 5); d <= new DateOnly(2026, 4, 19); d = d.AddDays(1))
        {
            var now = SimulationClock.FromAsOfDate(d);
            Assert.Equal(d, PoolClock.LastStatDate(now));
            Assert.Equal(d.AddDays(1), PoolClock.TodayEt(now));
        }
    }

    [Fact]
    public void TheStringAndDateOnlyFormsAgree()
    {
        Assert.Equal(
            SimulationClock.FromAsOfDate("2025-11-23"),
            SimulationClock.FromAsOfDate(new DateOnly(2025, 11, 23)));
    }
}
