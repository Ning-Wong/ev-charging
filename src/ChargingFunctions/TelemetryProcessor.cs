using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ChargingFunctions;

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

    // Required by ITableEntity, managed by the storage service
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

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
        string[] messages)
    {
        foreach (var raw in messages)
        {
            _log.LogInformation("Received: {Raw}", raw);

            var telemetry = JsonSerializer.Deserialize<CarTelemetry>(
                raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (telemetry is null || string.IsNullOrWhiteSpace(telemetry.DeviceId))
            {
                _log.LogWarning("Could not parse message, skipping");
                continue;
            }

            await _table.UpsertEntityAsync(new CarStateEntity
            {
                RowKey = telemetry.DeviceId,
                BatteryLevel = telemetry.BatteryLevel,
                IsCharging = telemetry.IsCharging,
                LastUpdated = telemetry.Timestamp
            });
        }
    }
}