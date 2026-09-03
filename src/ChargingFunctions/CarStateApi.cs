using System.Net;
using System.Text.Json.Serialization;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ChargingFunctions;

public record CarStateResponse(
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("batteryLevel")] int BatteryLevel,
    [property: JsonPropertyName("isCharging")] bool IsCharging,
    [property: JsonPropertyName("lastUpdated")] DateTimeOffset LastUpdated);

public class CarStateApi
{
    private readonly TableClient _table;
    private readonly ILogger<CarStateApi> _log;

    public CarStateApi(TableClient table, ILogger<CarStateApi> log)
    {
        _table = table;
        _log = log;
    }

    [Function(nameof(GetCarState))]
    public async Task<HttpResponseData> GetCarState(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cars/{deviceId}")]
        HttpRequestData req,
        string deviceId)
    {
        try
        {
            var entity = await _table.GetEntityAsync<CarStateEntity>("car", deviceId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new CarStateResponse(
                entity.Value.RowKey,
                entity.Value.BatteryLevel,
                entity.Value.IsCharging,
                entity.Value.LastUpdated));

            return response;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _log.LogWarning("No state found for device {DeviceId}", deviceId);

            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = $"No state for device '{deviceId}'" });
            return notFound;
        }
    }
}