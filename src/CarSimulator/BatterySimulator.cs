namespace CarSimulator;

// Simulates battery level changes while driving or charging.
public class BatterySimulator
{
    private readonly double _chargeRatePerMinute;
    private readonly double _drainRatePerMinute;

    private double _level;
    private bool _isCharging;
    private DateTimeOffset _lastTick;

    public BatterySimulator(
        double initialLevel,
        DateTimeOffset now,
        double chargeRatePerMinute = 50.0,
        double drainRatePerMinute = 30.0)
    {
        _level = Math.Clamp(initialLevel, 0.0, 100.0);
        _lastTick = now;
        _chargeRatePerMinute = chargeRatePerMinute;
        _drainRatePerMinute = drainRatePerMinute;
    }

    public int Level => (int)Math.Round(_level);

    public double ExactLevel => _level;

    public bool IsCharging => _isCharging;

    public bool IsFull => _level >= 100.0;

    public bool Advance(DateTimeOffset now)
    {
        var minutes = (now - _lastTick).TotalMinutes;
        _lastTick = now;

        _level += _isCharging
            ? _chargeRatePerMinute * minutes
            : -_drainRatePerMinute * minutes;

        _level = Math.Clamp(_level, 0.0, 100.0);

        if (_isCharging && _level >= 100.0)
        {
            _isCharging = false;
            return true;
        }

        return false;
    }

    public bool TryStartCharging(DateTimeOffset now)
    {
        Advance(now);

        if (_level >= 100.0) return false;

        _isCharging = true;
        return true;
    }

    public void StopCharging(DateTimeOffset now)
    {
        Advance(now);
        _isCharging = false;
    }

    public void Restore(double level, bool isCharging, DateTimeOffset now)
    {
        _level = Math.Clamp(level, 0.0, 100.0);
        _isCharging = isCharging;
        _lastTick = now;
    }
}