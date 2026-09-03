using System.Text;
using System.Text.Json;
using Microsoft.Azure.Devices.Client;

var connectionString =
    Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "IOTHUB_DEVICE_CONNECTION_STRING is not set. Run 'source .env' first.");

using var client = DeviceClient.CreateFromConnectionString(
    connectionString, TransportType.Mqtt);

var isCharging = false;
var batteryLevel = 50;

using var stateChanged = new SemaphoreSlim(0);

async Task SendTelemetryAsync()
{
    var telemetry = new
    {
        deviceId = "my-car",
        batteryLevel,
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
    isCharging = true;
    Console.WriteLine("Direct method: startCharging");
    stateChanged.Release();
    return Task.FromResult(new MethodResponse(
        Encoding.UTF8.GetBytes("""{"status":"charging started"}"""), 200));
}, null);

await client.SetMethodHandlerAsync("stopCharging", (request, _) =>
{
    isCharging = false;
    Console.WriteLine("Direct method: stopCharging");
    stateChanged.Release();
    return Task.FromResult(new MethodResponse(
        Encoding.UTF8.GetBytes("""{"status":"charging stopped"}"""), 200));
}, null);

var interval = TimeSpan.FromSeconds(15);
Console.WriteLine(
    $"Simulator started. Sending telemetry every {interval.TotalSeconds}s. Press Ctrl+C to stop.");

while (true)
{
    await SendTelemetryAsync();

    await stateChanged.WaitAsync(interval);
}