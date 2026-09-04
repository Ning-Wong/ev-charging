using System.Text;
using System.Text.Json;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;

var connectionString =
    Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "IOTHUB_DEVICE_CONNECTION_STRING is not set. Run 'source .env' first.");

using var client = DeviceClient.CreateFromConnectionString(
    connectionString, TransportType.Mqtt);

// --- Battery simulation parameters ---
var interval = TimeSpan.FromSeconds(15);
const double ChargeRatePerMinute = 50.0;
const double DrainRatePerMinute = 30.0;

var batteryLevel = 50.0;
var isCharging = false;
var lastTick = DateTimeOffset.UtcNow;

string? scheduleTime = null;          // "02:00"
string? scheduleZone = null;          // "Pacific/Auckland"
int? remainingRuns = null;            // null means repeat forever
DateTimeOffset? nextScheduledRun = null;

bool? lastReportedCharging = null;

using var stateChanged = new SemaphoreSlim(0);

void AdvanceBattery()
{
    var now = DateTimeOffset.UtcNow;
    var minutes = (now - lastTick).TotalMinutes;
    lastTick = now;

    batteryLevel += isCharging
        ? ChargeRatePerMinute * minutes
        : -DrainRatePerMinute * minutes;

    batteryLevel = Math.Clamp(batteryLevel, 0.0, 100.0);

    if (isCharging && batteryLevel >= 100.0)
    {
        isCharging = false;
        Console.WriteLine("Battery full, charging stopped automatically");
    }
}

DateTimeOffset? ComputeNextRun(string? time, string? zone, int? runsLeft)
{
    if (string.IsNullOrWhiteSpace(time) || string.IsNullOrWhiteSpace(zone))
        return null;

    if (runsLeft is <= 0)
        return null;

    if (!TimeSpan.TryParse(time, out var timeOfDay))
    {
        Console.WriteLine($"Invalid schedule time '{time}', ignoring");
        return null;
    }

    TimeZoneInfo tz;
    try
    {
        tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
    }
    catch (TimeZoneNotFoundException)
    {
        Console.WriteLine($"Unknown time zone '{zone}', ignoring schedule");
        return null;
    }

    var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime;

    for (var dayOffset = 0; dayOffset <= 2; dayOffset++)
    {
        var candidate = localNow.Date.AddDays(dayOffset) + timeOfDay;
        if (candidate <= localNow) continue;

        var unspecified = DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified);

        if (tz.IsInvalidTime(unspecified))
        {
            Console.WriteLine(
                $"{candidate:yyyy-MM-dd HH:mm} does not exist in {zone} (clocks jump forward), trying the next day");
            continue;
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, tz), TimeSpan.Zero);
    }

    return null;
}

async Task ReportStateAsync()
{
    object? schedulePayload = scheduleTime is null
        ? null
        : new
        {
            startTime = scheduleTime,
            timeZone = scheduleZone,
            remainingRuns
        };

    var json = JsonSerializer.Serialize(new
    {
        isCharging,
        batteryLevel = (int)Math.Round(batteryLevel),
        chargingSchedule = schedulePayload
    });

    await client.UpdateReportedPropertiesAsync(new TwinCollection(json));
    lastReportedCharging = isCharging;
}

async Task ApplyDesiredAsync(TwinCollection desired)
{
    if (!desired.Contains("chargingSchedule")) return;

    var raw = desired["chargingSchedule"]?.ToString();

    if (string.IsNullOrWhiteSpace(raw) || raw == "null")
    {
        scheduleTime = null;
        scheduleZone = null;
        remainingRuns = null;
        nextScheduledRun = null;
        Console.WriteLine("Charging schedule cleared");
    }
    else
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        scheduleTime = root.TryGetProperty("startTime", out JsonElement t) ? t.GetString() : null;
        scheduleZone = root.TryGetProperty("timeZone", out JsonElement z) ? z.GetString() : null;

        remainingRuns = root.TryGetProperty("remainingRuns", out JsonElement r)
                        && r.ValueKind == JsonValueKind.Number
            ? r.GetInt32()
            : null;

        nextScheduledRun = ComputeNextRun(scheduleTime, scheduleZone, remainingRuns);

        var runsLabel = remainingRuns?.ToString() ?? "unlimited";
        Console.WriteLine(nextScheduledRun is null
            ? $"Charging schedule {scheduleTime} ({scheduleZone}) could not be resolved"
            : $"Charging schedule set to {scheduleTime} ({scheduleZone}), runs={runsLabel}, next run at {nextScheduledRun:u}");
    }

    await ReportStateAsync();
}

async Task SendTelemetryAsync()
{
    var telemetry = new
    {
        deviceId = "my-car",
        batteryLevel = (int)Math.Round(batteryLevel),
        isCharging,
        timestamp = DateTimeOffset.UtcNow
    };

    var payload = JsonSerializer.Serialize(telemetry);

    using var message = new Message(Encoding.UTF8.GetBytes(payload))
    {
        ContentType = "application/json",
        ContentEncoding = "utf-8"
    };

    await client.SendEventAsync(message);
    Console.WriteLine($"Sent: {payload}");
}

await client.SetMethodHandlerAsync("startCharging", (request, _) =>
{
    AdvanceBattery();

    if (batteryLevel >= 100.0)
    {
        Console.WriteLine("Direct method: startCharging rejected, battery already full");
        return Task.FromResult(new MethodResponse(
            Encoding.UTF8.GetBytes("""{"status":"battery already full"}"""), 409));
    }

    isCharging = true;
    Console.WriteLine("Direct method: startCharging");
    stateChanged.Release();
    return Task.FromResult(new MethodResponse(
        Encoding.UTF8.GetBytes("""{"status":"charging started"}"""), 200));
}, null);

await client.SetMethodHandlerAsync("stopCharging", (request, _) =>
{
    AdvanceBattery();
    isCharging = false;
    Console.WriteLine("Direct method: stopCharging");
    stateChanged.Release();
    return Task.FromResult(new MethodResponse(
        Encoding.UTF8.GetBytes("""{"status":"charging stopped"}"""), 200));
}, null);

var twin = await client.GetTwinAsync();

if (twin.Properties.Reported.Contains("isCharging"))
{
    isCharging = (bool)twin.Properties.Reported["isCharging"];
    Console.WriteLine($"Restored charging state from twin: {isCharging}");
}

if (twin.Properties.Reported.Contains("batteryLevel"))
{
    batteryLevel = (int)twin.Properties.Reported["batteryLevel"];
    Console.WriteLine($"Restored battery level from twin: {batteryLevel}%");
}

var restoredRuns = (int?)null;
if (twin.Properties.Reported.Contains("chargingSchedule"))
{
    var reportedRaw = twin.Properties.Reported["chargingSchedule"]?.ToString();
    if (!string.IsNullOrWhiteSpace(reportedRaw) && reportedRaw != "null")
    {
        using var doc = JsonDocument.Parse(reportedRaw);
        if (doc.RootElement.TryGetProperty("remainingRuns", out JsonElement r)
            && r.ValueKind == JsonValueKind.Number)
        {
            restoredRuns = r.GetInt32();
        }
    }
}

lastTick = DateTimeOffset.UtcNow;

await ApplyDesiredAsync(twin.Properties.Desired);

if (restoredRuns is { } runs && remainingRuns is not null && runs < remainingRuns)
{
    remainingRuns = runs;
    nextScheduledRun = ComputeNextRun(scheduleTime, scheduleZone, remainingRuns);
    Console.WriteLine($"Restored countdown from twin: {runs} run(s) left");
    await ReportStateAsync();
}

await client.SetDesiredPropertyUpdateCallbackAsync(
    async (desired, _) => await ApplyDesiredAsync(desired), null);

Console.WriteLine(
    $"Simulator started. Sending telemetry every {interval.TotalSeconds}s. Press Ctrl+C to stop.");

while (true)
{
    AdvanceBattery();

    if (nextScheduledRun is { } due && DateTimeOffset.UtcNow >= due)
    {
        if (!isCharging && batteryLevel < 100.0)
        {
            isCharging = true;
            Console.WriteLine($"Scheduled charging started ({scheduleTime} {scheduleZone})");
        }
        else
        {
            Console.WriteLine("Scheduled time reached, but charging was not needed");
        }

        if (remainingRuns is { } left)
        {
            remainingRuns = left - 1;
            Console.WriteLine($"Runs left: {remainingRuns}");
        }

        nextScheduledRun = ComputeNextRun(scheduleTime, scheduleZone, remainingRuns);
        Console.WriteLine(nextScheduledRun is null
            ? "Schedule finished, no further runs"
            : $"Next scheduled run at {nextScheduledRun:u}");

        await ReportStateAsync();
    }

    await SendTelemetryAsync();

    if (lastReportedCharging != isCharging)
    {
        await ReportStateAsync();
    }

    await stateChanged.WaitAsync(interval);
}