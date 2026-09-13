using System.Text;
using System.Text.Json;
using CarSimulator;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;

// Connect to IoT Hub as the simulated car.
var connectionString =
    Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "IOTHUB_DEVICE_CONNECTION_STRING is not set. Run 'source .env' first.");

using var client = DeviceClient.CreateFromConnectionString(
    connectionString, TransportType.Mqtt);

var interval = TimeSpan.FromSeconds(15);

var battery = new BatterySimulator(
    initialLevel: 50.0,
    now: DateTimeOffset.UtcNow,
    chargeRatePerMinute: 50.0,
    drainRatePerMinute: 30.0);

string? scheduleTime = null;          // "02:00"
string? scheduleZone = null;          // "Pacific/Auckland"
int? remainingRuns = null;            // null means repeat forever
DateTimeOffset? nextScheduledRun = null;

bool? lastReportedCharging = null;

using var stateChanged = new SemaphoreSlim(0);

// Log the next scheduled charging run.
void LogNextRun()
{
    Console.WriteLine(nextScheduledRun is null
        ? "No further scheduled runs"
        : $"Next scheduled run at {nextScheduledRun:u}");
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
        isCharging = battery.IsCharging,
        batteryLevel = battery.Level,
        chargingSchedule = schedulePayload
    });

    await client.UpdateReportedPropertiesAsync(new TwinCollection(json));
    lastReportedCharging = battery.IsCharging;
}

// Apply schedule changes received through the device twin.
async Task ApplyDesiredAsync(TwinCollection desired)
{
    if (!desired.Contains("chargingSchedule")) return;

    string? raw = desired["chargingSchedule"]?.ToString();

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
        JsonElement root = doc.RootElement;

        scheduleTime = root.TryGetProperty("startTime", out JsonElement t) ? t.GetString() : null;
        scheduleZone = root.TryGetProperty("timeZone", out JsonElement z) ? z.GetString() : null;

        remainingRuns = root.TryGetProperty("remainingRuns", out JsonElement r)
                        && r.ValueKind == JsonValueKind.Number
            ? r.GetInt32()
            : null;

        nextScheduledRun = ScheduleCalculator.ComputeNextRun(
            scheduleTime, scheduleZone, remainingRuns, DateTimeOffset.UtcNow);

        var runsLabel = remainingRuns?.ToString() ?? "unlimited";
        Console.WriteLine(
            $"Charging schedule set to {scheduleTime} ({scheduleZone}), runs={runsLabel}");
        LogNextRun();
    }

    await ReportStateAsync();
}

async Task SendTelemetryAsync()
{
    var telemetry = new
    {
        deviceId = "Model-Y",
        batteryLevel = battery.Level,
        isCharging = battery.IsCharging,
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

// Handle direct charging commands from the cloud.
await client.SetMethodHandlerAsync("startCharging", (request, _) =>
{
    if (!battery.TryStartCharging(DateTimeOffset.UtcNow))
    {
        Console.WriteLine("Direct method: startCharging rejected, battery already full");
        return Task.FromResult(new MethodResponse(
            Encoding.UTF8.GetBytes("""{"status":"battery already full"}"""), 409));
    }

    Console.WriteLine("Direct method: startCharging");
    stateChanged.Release();
    return Task.FromResult(new MethodResponse(
        Encoding.UTF8.GetBytes("""{"status":"charging started"}"""), 200));
}, null);

await client.SetMethodHandlerAsync("stopCharging", (request, _) =>
{
    battery.StopCharging(DateTimeOffset.UtcNow);
    Console.WriteLine("Direct method: stopCharging");
    stateChanged.Release();
    return Task.FromResult(new MethodResponse(
        Encoding.UTF8.GetBytes("""{"status":"charging stopped"}"""), 200));
}, null);

var twin = await client.GetTwinAsync();

// Restore the last known car state from the device twin.
var restoredLevel = 50.0;
var restoredCharging = false;

if (twin.Properties.Reported.Contains("isCharging"))
{
    restoredCharging = (bool)twin.Properties.Reported["isCharging"];
    Console.WriteLine($"Restored charging state from twin: {restoredCharging}");
}

if (twin.Properties.Reported.Contains("batteryLevel"))
{
    restoredLevel = (int)twin.Properties.Reported["batteryLevel"];
    Console.WriteLine($"Restored battery level from twin: {restoredLevel}%");
}

battery.Restore(restoredLevel, restoredCharging, DateTimeOffset.UtcNow);

var restoredRuns = (int?)null;
if (twin.Properties.Reported.Contains("chargingSchedule"))
{
    string? reportedRaw = twin.Properties.Reported["chargingSchedule"]?.ToString();
    if (!string.IsNullOrWhiteSpace(reportedRaw) && reportedRaw != "null")
    {
        using var doc = JsonDocument.Parse(reportedRaw);
        JsonElement reportedRoot = doc.RootElement;

        if (reportedRoot.TryGetProperty("remainingRuns", out JsonElement r)
            && r.ValueKind == JsonValueKind.Number)
        {
            restoredRuns = r.GetInt32();
        }
    }
}

await ApplyDesiredAsync(twin.Properties.Desired);

if (restoredRuns is { } runs && remainingRuns is not null && runs < remainingRuns)
{
    remainingRuns = runs;
    nextScheduledRun = ScheduleCalculator.ComputeNextRun(
        scheduleTime, scheduleZone, remainingRuns, DateTimeOffset.UtcNow);

    Console.WriteLine($"Restored countdown from twin: {runs} run(s) left");
    LogNextRun();
    await ReportStateAsync();
}

await client.SetDesiredPropertyUpdateCallbackAsync(
    async (desired, _) => await ApplyDesiredAsync(desired), null);

// Run the simulator loop and publish telemetry.
Console.WriteLine(
    $"Simulator started. Sending telemetry every {interval.TotalSeconds}s. Press Ctrl+C to stop.");

while (true)
{
    if (battery.Advance(DateTimeOffset.UtcNow))
    {
        Console.WriteLine("Battery full, charging stopped automatically");
    }

    if (nextScheduledRun is { } due && DateTimeOffset.UtcNow >= due)
    {
        if (battery.TryStartCharging(DateTimeOffset.UtcNow))
        {
            Console.WriteLine($"Scheduled charging started ({scheduleTime} {scheduleZone})");
        }
        else
        {
            Console.WriteLine("Scheduled time reached, but the battery is already full");
        }

        if (remainingRuns is { } left)
        {
            remainingRuns = left - 1;
            Console.WriteLine($"Runs left: {remainingRuns}");
        }

        nextScheduledRun = ScheduleCalculator.ComputeNextRun(
            scheduleTime, scheduleZone, remainingRuns, DateTimeOffset.UtcNow);

        LogNextRun();
        await ReportStateAsync();
    }

    await SendTelemetryAsync();

    if (lastReportedCharging != battery.IsCharging)
    {
        await ReportStateAsync();
    }

    await stateChanged.WaitAsync(interval);
}