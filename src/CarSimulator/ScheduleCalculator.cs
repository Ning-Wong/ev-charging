namespace CarSimulator;

public static class ScheduleCalculator
{
    public static DateTimeOffset? ComputeNextRun(
        string? time,
        string? zone,
        int? runsLeft,
        DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(time) || string.IsNullOrWhiteSpace(zone))
            return null;

        if (runsLeft is <= 0)
            return null;

        if (!TimeSpan.TryParse(time, out var timeOfDay)
            || timeOfDay < TimeSpan.Zero
            || timeOfDay >= TimeSpan.FromDays(1))
        {
            return null;
        }

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }

        var localNow = TimeZoneInfo.ConvertTime(utcNow, tz).DateTime;

        for (var dayOffset = 0; dayOffset <= 2; dayOffset++)
        {
            var candidate = localNow.Date.AddDays(dayOffset) + timeOfDay;
            if (candidate <= localNow) continue;

            var unspecified = DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified);

            if (tz.IsInvalidTime(unspecified))
            {
                var shifted = unspecified;
                var resolved = false;

                for (var minutes = 1; minutes <= 180; minutes++)
                {
                    shifted = unspecified.AddMinutes(minutes);
                    if (!tz.IsInvalidTime(shifted))
                    {
                        resolved = true;
                        break;
                    }
                }

                if (!resolved) continue;
                unspecified = shifted;
            }

            return new DateTimeOffset(
                TimeZoneInfo.ConvertTimeToUtc(unspecified, tz), TimeSpan.Zero);
        }
        return null;
    }
}