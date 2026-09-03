using System.Text;
using System.Text.Json;
using Microsoft.Azure.Devices.Client;

var connectionString =
    Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "IOTHUB_DEVICE_CONNECTION_STRING is not set. Run 'source .env' first.");

using var client = DeviceClient.CreateFromConnectionString(
    connectionString, TransportType.Mqtt);

// --- Battery simulation parameters ---
var interval = TimeSpan.FromSeconds(15);
const double ChargeRatePerMinute = 50.0;   // full charge in 2 minutes
const double DrainRatePerMinute = 30.0;     // idle drain

var batteryLevel = 50.0;
var isCharging = false;
var lastTick = DateTimeOffset.UtcNow;

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

Console.WriteLine(
    $"Simulator started. Sending telemetry every {interval.TotalSeconds}s. Press Ctrl+C to stop.");

while (true)
{
    AdvanceBattery();
    await SendTelemetryAsync();
    await stateChanged.WaitAsync(interval);
}