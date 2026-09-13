using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

namespace ChargingFunctions.Auth;

public sealed class AuthResult
{
    public string? UserId { get; private init; }
    public HttpResponseData? Failure { get; private init; }

    public bool Ok => Failure is null;

    public static AuthResult Allow(string userId) => new() { UserId = userId };
    public static AuthResult Deny(HttpResponseData response) => new() { Failure = response };
}

public class RequestAuth
{
    private readonly TokenService _tokens;
    private readonly OwnershipService _ownership;

    public RequestAuth(TokenService tokens, OwnershipService ownership)
    {
        _tokens = tokens;
        _ownership = ownership;
    }

    public async Task<AuthResult> RequireUserAsync(HttpRequestData req)
    {
        var header = req.Headers.TryGetValues("Authorization", out var values)
            ? values.FirstOrDefault()
            : null;

        var userId = _tokens.ValidateAndGetUserId(header);

        if (userId is null)
        {
            return AuthResult.Deny(await Responses.ErrorAsync(
                req, HttpStatusCode.Unauthorized, "Sign in to continue."));
        }

        return AuthResult.Allow(userId);
    }

    public async Task<AuthResult> RequireOwnerAsync(HttpRequestData req, string deviceId)
    {
        var auth = await RequireUserAsync(req);
        if (!auth.Ok) return auth;

        if (!await _ownership.OwnsAsync(auth.UserId!, deviceId))
        {
            return AuthResult.Deny(await Responses.ErrorAsync(
                req, HttpStatusCode.Forbidden, "You do not have access to this car."));
        }

        return auth;
    }
}