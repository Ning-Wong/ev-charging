using CarSimulator;

namespace CarSimulator.Tests;

// Tests battery charging and drain behavior.
public class BatterySimulatorTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Charges_over_time_when_charging()
    {
        var battery = new BatterySimulator(50.0, T0, chargeRatePerMinute: 50.0);

        battery.TryStartCharging(T0);
        battery.Advance(T0.AddMinutes(0.5));

        Assert.Equal(75, battery.Level);
        Assert.True(battery.IsCharging);
    }

    [Fact]
    public void Level_depends_on_elapsed_time_not_on_tick_count()
    {
        var oneStep = new BatterySimulator(0.0, T0, chargeRatePerMinute: 60.0);
        oneStep.TryStartCharging(T0);
        oneStep.Advance(T0.AddMinutes(1));

        var manySteps = new BatterySimulator(0.0, T0, chargeRatePerMinute: 60.0);
        manySteps.TryStartCharging(T0);
        for (var i = 1; i <= 10; i++)
        {
            manySteps.Advance(T0.AddSeconds(i * 6));
        }

        Assert.Equal(oneStep.Level, manySteps.Level);
    }

    [Fact]
    public void Never_exceeds_one_hundred()
    {
        var battery = new BatterySimulator(90.0, T0, chargeRatePerMinute: 50.0);

        battery.TryStartCharging(T0);
        battery.Advance(T0.AddMinutes(10));

        Assert.Equal(100, battery.Level);
    }

    [Fact]
    public void Stops_charging_automatically_when_full()
    {
        var battery = new BatterySimulator(99.0, T0, chargeRatePerMinute: 50.0);
        battery.TryStartCharging(T0);

        var becameFull = battery.Advance(T0.AddMinutes(1));

        Assert.True(becameFull);
        Assert.False(battery.IsCharging);
        Assert.Equal(100, battery.Level);
    }
}