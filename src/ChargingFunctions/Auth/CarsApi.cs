using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ChargingFunctions.Auth;

public record ClaimRequest(
    [property: JsonPropertyName("deviceId")] string? DeviceId,
    [property: JsonPropertyName("claimCode")] string? ClaimCode);

public class CarsApi
{
    private const string ClaimCodeKey = "claimCode";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly RegistryManager _registry;
    private readonly OwnershipService _ownership;
    private readonly RequestAuth _auth;
    private readonly ILogger<CarsApi> _log;

    public CarsApi(RegistryManager registry, OwnershipService ownership, RequestAuth auth, ILogger<CarsApi> log)
    {
        _registry = registry;
        _ownership = ownership;
        _auth = auth;
        _log = log;
    }

    [Function(nameof(ListCars))]
    public async Task<HttpResponseData> ListCars(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cars")]
        HttpRequestData req)
    {
        var authResult = await _auth.RequireUserAsync(req);
        if (!authResult.Ok) return authResult.Failure!;

        var devices = await _ownership.ListDevicesAsync(authResult.UserId!);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(devices);
        return response;
    }

    [Function(nameof(ClaimCar))]
    public async Task<HttpResponseData> ClaimCar(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cars/claim")]
        HttpRequestData req)
    {
        var authResult = await _auth.RequireUserAsync(req);
        if (!authResult.Ok) return authResult.Failure!;

        var userId = authResult.UserId!;

        var body = await req.ReadAsStringAsync();
        ClaimRequest? claim = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                claim = JsonSerializer.Deserialize<ClaimRequest>(body, JsonOptions);
            }
            catch (JsonException)
            {
                // Falls through to the generic failure below
            }
        }

        if (claim is null
            || string.IsNullOrWhiteSpace(claim.DeviceId)
            || string.IsNullOrWhiteSpace(claim.ClaimCode))
        {
            return await ClaimFailed(req);
        }

        if (await _ownership.IsClaimedAsync(claim.DeviceId))
        {
            return await Responses.ErrorAsync(req, HttpStatusCode.Conflict,
                "That car has already been claimed.");
        }

        var expected = await ReadClaimCodeAsync(claim.DeviceId);

        // Same response whether the device is unknown or the code is wrong
        if (expected is null || !string.Equals(expected, claim.ClaimCode, StringComparison.Ordinal))
        {
            _log.LogWarning("Failed claim attempt by {UserId} for {DeviceId}", userId, claim.DeviceId);
            return await ClaimFailed(req);
        }

        await _ownership.ClaimAsync(userId, claim.DeviceId);
        _log.LogInformation("{UserId} claimed {DeviceId}", userId, claim.DeviceId);

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private async Task<string?> ReadClaimCodeAsync(string deviceId)
    {
        try
        {
            var twin = await _registry.GetTwinAsync(deviceId);

            if (twin is null || !twin.Properties.Desired.Contains(ClaimCodeKey))
                return null;

            // TwinCollection returns dynamic; pin the type immediately
            string? code = twin.Properties.Desired[ClaimCodeKey]?.ToString();

            return string.IsNullOrWhiteSpace(code) ? null : code.Trim('"');
        }
        catch (DeviceNotFoundException)
        {
            return null;
        }
    }

    private static Task<HttpResponseData> ClaimFailed(HttpRequestData req) =>
        Responses.ErrorAsync(req, HttpStatusCode.BadRequest,
            "Unknown car or incorrect claim code.");
}