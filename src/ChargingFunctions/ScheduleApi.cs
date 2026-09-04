using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ChargingFunctions;

public record ChargingSchedule(
    [property: JsonPropertyName("startTime")] string StartTime,
    [property: JsonPropertyName("timeZone")] string TimeZone,
    [property: JsonPropertyName("remainingRuns")] int? RemainingRuns);

public record ScheduleResponse(
    [property: JsonPropertyName("startTime")] string? StartTime,
    [property: JsonPropertyName("timeZone")] string? TimeZone,
    [property: JsonPropertyName("remainingRuns")] int? RemainingRuns,
    [property: JsonPropertyName("runsLeftOnCar")] int? RunsLeftOnCar,
    [property: JsonPropertyName("acknowledgedByCar")] bool AcknowledgedByCar);

public class ScheduleApi
{
    private const string TwinKey = "chargingSchedule";
    private const int MaxRuns = 999999;

    private readonly RegistryManager _registry;
    private readonly ILogger<ScheduleApi> _log;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public ScheduleApi(RegistryManager registry, ILogger<ScheduleApi> log)
    {
        _registry = registry;
        _log = log;
    }

    [Function(nameof(GetSchedule))]
    public async Task<HttpResponseData> GetSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cars/{deviceId}/schedule")]
        HttpRequestData req,
        string deviceId)
    {
        var twin = await _registry.GetTwinAsync(deviceId);

        var desired = ReadSchedule(twin.Properties.Desired);
        var reported = ReadSchedule(twin.Properties.Reported);

        var acknowledged = desired is not null
            && reported is not null
            && desired.StartTime == reported.StartTime
            && desired.TimeZone == reported.TimeZone;

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new ScheduleResponse(
            desired?.StartTime,
            desired?.TimeZone,
            desired?.RemainingRuns,
            reported?.RemainingRuns,
            acknowledged));

        return response;
    }

    [Function(nameof(SetSchedule))]
    public async Task<HttpResponseData> SetSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "cars/{deviceId}/schedule")]
        HttpRequestData req,
        string deviceId)
    {
        var body = await req.ReadAsStringAsync();
        ChargingSchedule? schedule = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                schedule = JsonSerializer.Deserialize<ChargingSchedule>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return await BadRequest(req, "Body must be JSON.");
            }
        }

        var clearing = schedule is null || string.IsNullOrWhiteSpace(schedule.StartTime);

        if (!clearing)
        {
            if (!TimeSpan.TryParse(schedule!.StartTime, out var parsed)
                || parsed < TimeSpan.Zero
                || parsed >= TimeSpan.FromDays(1))
            {
                return await BadRequest(req, "startTime must be a time of day such as '02:00'.");
            }

            if (string.IsNullOrWhiteSpace(schedule.TimeZone))
            {
                return await BadRequest(req, "timeZone is required, e.g. 'Pacific/Auckland'.");
            }

            if (schedule.RemainingRuns is { } runs && (runs < 1 || runs > MaxRuns))
            {
                return await BadRequest(
                    req, $"remainingRuns must be between 1 and {MaxRuns}, or omitted for a repeating schedule.");
            }
        }

        var twin = await _registry.GetTwinAsync(deviceId);

        var patch = JsonSerializer.Serialize(new
        {
            properties = new
            {
                desired = new Dictionary<string, object?>
                {
                    [TwinKey] = clearing
                        ? null
                        : new
                        {
                            startTime = schedule!.StartTime,
                            timeZone = schedule.TimeZone,
                            remainingRuns = schedule.RemainingRuns
                        }
                }
            }
        });

        await _registry.UpdateTwinAsync(deviceId, patch, twin.ETag);

        if (clearing)
        {
            _log.LogInformation("Cleared charging schedule for {DeviceId}", deviceId);
        }
        else
        {
            _log.LogInformation(
                "Set charging schedule for {DeviceId} to {StartTime} {TimeZone}, runs={Runs}",
                deviceId, schedule!.StartTime, schedule.TimeZone,
                schedule.RemainingRuns?.ToString() ?? "unlimited");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new ScheduleResponse(
            clearing ? null : schedule!.StartTime,
            clearing ? null : schedule!.TimeZone,
            clearing ? null : schedule!.RemainingRuns,
            null,
            false));

        return response;
    }

    private static ChargingSchedule? ReadSchedule(TwinCollection properties)
    {
        if (!properties.Contains(TwinKey)) return null;

        var raw = properties[TwinKey]?.ToString();
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;

        try
        {
            return JsonSerializer.Deserialize<ChargingSchedule>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}