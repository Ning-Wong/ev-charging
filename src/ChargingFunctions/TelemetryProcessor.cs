using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Messaging.EventHubs;

namespace ChargingFunctions;

// Models telemetry received from the car.
public record CarTelemetry(
    string DeviceId,
    int BatteryLevel,
    bool IsCharging,
    DateTimeOffset Timestamp);

public class CarStateEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "car";
    public string RowKey { get; set; } = default!;
    public int BatteryLevel { get; set; }
    public bool IsCharging { get; set; }
    public DateTimeOffset LastUpdated { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

// Processes IoT Hub telemetry events.
public class TelemetryProcessor
{
    private readonly TableClient _table;
    private readonly ILogger<TelemetryProcessor> _log;

    public TelemetryProcessor(TableClient table, ILogger<TelemetryProcessor> log)
    {
        _table = table;
        _log = log;
    }

    [Function(nameof(TelemetryProcessor))]
    public async Task Run(
        [EventHubTrigger(
            "messages/events",
            Connection = "IoTHubEventEndpoint",
            ConsumerGroup = "functions")]
        EventData[] events)
    {
        foreach (var e in events)
        {
            var raw = e.EventBody.ToString();
            _log.LogInformation("Received: {Raw}", raw);

            if (!e.SystemProperties.TryGetValue(
                    "iothub-connection-device-id", out var deviceIdObj)
                || deviceIdObj is not string deviceId
                || string.IsNullOrWhiteSpace(deviceId))
            {
                _log.LogWarning("Message without a device id, skipping");
                continue;
            }

            var telemetry = JsonSerializer.Deserialize<CarTelemetry>(
                raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (telemetry is null || string.IsNullOrWhiteSpace(deviceId))
            {
                _log.LogWarning("Could not parse message, skipping");
                continue;
            }

            await _table.UpsertEntityAsync(new CarStateEntity
            {
                RowKey = deviceId,
                BatteryLevel = telemetry.BatteryLevel,
                IsCharging = telemetry.IsCharging,
                LastUpdated = telemetry.Timestamp
            });
        }
    }
}