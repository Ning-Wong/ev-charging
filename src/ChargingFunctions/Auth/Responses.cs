using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

namespace ChargingFunctions.Auth;

internal static class Responses
{
    public static async Task<HttpResponseData> ErrorAsync(
        HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}