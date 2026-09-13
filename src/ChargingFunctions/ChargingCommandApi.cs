using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Devices.Common.Exceptions;
using ChargingFunctions.Auth;

namespace ChargingFunctions;

// Models the result of a charging command.
public record CommandResponse(
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("deviceStatus")] int DeviceStatus);

// HTTP API for direct charging commands.
public class ChargingCommandApi
{
    private readonly ServiceClient _serviceClient;
    private readonly RequestAuth _auth;
    private readonly ILogger<ChargingCommandApi> _log;

    public ChargingCommandApi(ServiceClient serviceClient, RequestAuth auth, ILogger<ChargingCommandApi> log)
    {
        _serviceClient = serviceClient;
        _auth = auth;
        _log = log;
    }

    [Function(nameof(StartCharging))]
    public Task<HttpResponseData> StartCharging(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cars/{deviceId}/charging/start")]
        HttpRequestData req,
        string deviceId)
        => InvokeAsync(req, deviceId, "startCharging");

    [Function(nameof(StopCharging))]
    public Task<HttpResponseData> StopCharging(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cars/{deviceId}/charging/stop")]
        HttpRequestData req,
        string deviceId)
        => InvokeAsync(req, deviceId, "stopCharging");

    private async Task<HttpResponseData> InvokeAsync(
        HttpRequestData req, string deviceId, string methodName)
    {
        var authResult = await _auth.RequireOwnerAsync(req, deviceId);
        if (!authResult.Ok) return authResult.Failure!;
        
        var method = new CloudToDeviceMethod(methodName)
        {
            ResponseTimeout = TimeSpan.FromSeconds(30)
        };

        try
        {
            var result = await _serviceClient.InvokeDeviceMethodAsync(deviceId, method);
            _log.LogInformation(
                "Invoked {Method} on {DeviceId}, device returned {Status}",
                methodName, deviceId, result.Status);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new CommandResponse(
                deviceId, methodName, result.Status == 200, result.Status));
            return response;
        }
        catch (DeviceNotFoundException)
        {
            _log.LogWarning("Device {DeviceId} is not connected", deviceId);

            var offline = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            await offline.WriteAsJsonAsync(new
            {
                error = $"Device '{deviceId}' is not connected"
            });
            return offline;
        }
    }
}