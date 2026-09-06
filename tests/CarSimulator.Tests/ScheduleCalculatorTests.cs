using CarSimulator;

namespace CarSimulator.Tests;

// Tests schedule calculation across time zones and DST.
public class ScheduleCalculatorTests
{
    private const string Auckland = "Pacific/Auckland";

    [Fact]
    public void Fires_tomorrow_when_todays_time_has_already_passed()
    {
        // 2026-09-10 15:00 NZST is 2026-09-10 03:00 UTC
        var utcNow = new DateTimeOffset(2026, 9, 10, 3, 0, 0, TimeSpan.Zero);

        var next = ScheduleCalculator.ComputeNextRun("14:00", Auckland, null, utcNow);

        // The next 14:00 NZST is on the 11th, which is 02:00 UTC
        Assert.Equal(
            new DateTimeOffset(2026, 9, 11, 2, 0, 0, TimeSpan.Zero),
            next);
    }

    [Fact]
    public void Skips_the_hour_that_does_not_exist_on_the_dst_transition()
    {
        // New Zealand enters daylight saving at 02:00 on the last Sunday of
        // September, so 2026-09-27 02:00 never happens. The exercise example
        // is literally 2 AM, so this branch matters.
        // 2026-09-26 12:00 NZST is 2026-09-26 00:00 UTC
        var utcNow = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

        var next = ScheduleCalculator.ComputeNextRun("02:00", Auckland, null, utcNow);

        Assert.NotNull(next);

        var localNext = TimeZoneInfo.ConvertTime(
            next!.Value, TimeZoneInfo.FindSystemTimeZoneById(Auckland));

        // Rolled forward to the 27th, not at 02:00 wall-clock time
        Assert.Equal(27, localNext.Day);
        Assert.Equal(3, localNext.Hour);
    }
}