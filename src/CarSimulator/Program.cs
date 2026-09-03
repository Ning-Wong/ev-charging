using System.Text;
using System.Text.Json;
using Microsoft.Azure.Devices.Client;

var connectionString =
    Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "IOTHUB_DEVICE_CONNECTION_STRING is not set. Run 'source .env' first.");

using var client = DeviceClient.CreateFromConnectionString(
    connectionString, TransportType.Mqtt);

var interval = TimeSpan.FromSeconds(15);
Console.WriteLine(
    $"Simulator started. Sending telemetry every {interval.TotalSeconds}s. Press Ctrl+C to stop.");

while (true)
{
    var telemetry = new
    {
        deviceId = "my-car",
        batteryLevel = 50,
        isCharging = false,
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

    await Task.Delay(interval);
}