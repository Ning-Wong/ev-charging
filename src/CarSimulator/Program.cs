using System.Text;
using Microsoft.Azure.Devices.Client;

var connectionString =
    Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "IOTHUB_DEVICE_CONNECTION_STRING is not set. Run 'source .env' first.");

using var client = DeviceClient.CreateFromConnectionString(
    connectionString, TransportType.Mqtt);

var payload = """{"hello":"world"}""";
using var message = new Message(Encoding.UTF8.GetBytes(payload));

await client.SendEventAsync(message);
Console.WriteLine($"Sent: {payload}");