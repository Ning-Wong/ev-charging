using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ChargingFunctions.Auth;

public record CredentialsRequest(
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("password")] string? Password);

public record LoginResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("username")] string Username);

public class AuthApi
{
    private const int MinPasswordLength = 8;
    private const int MaxUsernameLength = 64;

    private static readonly (string Hash, string Salt) Decoy =
        PasswordHasher.Create(Guid.NewGuid().ToString());

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly Tables _tables;
    private readonly TokenService _tokens;
    private readonly ILogger<AuthApi> _log;

    public AuthApi(Tables tables, TokenService tokens, ILogger<AuthApi> log)
    {
        _tables = tables;
        _tokens = tokens;
        _log = log;
    }

    [Function(nameof(Register))]
    public async Task<HttpResponseData> Register(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/register")]
        HttpRequestData req)
    {
        var credentials = await ReadCredentialsAsync(req);

        if (credentials is null)
            return await Responses.ErrorAsync(req, HttpStatusCode.BadRequest, "Body must be JSON.");

        var username = credentials.Username?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(username) || username.Length > MaxUsernameLength)
        {
            return await Responses.ErrorAsync(req, HttpStatusCode.BadRequest,
                $"Username is required and must be at most {MaxUsernameLength} characters.");
        }

        // Table Storage rejects these characters in a row key
        if (username.IndexOfAny(['/', '\\', '#', '?']) >= 0)
        {
            return await Responses.ErrorAsync(req, HttpStatusCode.BadRequest,
                @"Username cannot contain / \ # or ?");
        }

        if (credentials.Password is not { Length: >= MinPasswordLength })
        {
            return await Responses.ErrorAsync(req, HttpStatusCode.BadRequest,
                $"Password must be at least {MinPasswordLength} characters.");
        }

        var (hash, salt) = PasswordHasher.Create(credentials.Password);

        try
        {
            await _tables.Users.AddEntityAsync(new UserEntity
            {
                RowKey = username,
                PasswordHash = hash,
                PasswordSalt = salt,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return await Responses.ErrorAsync(req, HttpStatusCode.Conflict,
                "That username is taken.");
        }

        _log.LogInformation("Registered user {Username}", username);

        return req.CreateResponse(HttpStatusCode.Created);
    }

    [Function(nameof(Login))]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")]
        HttpRequestData req)
    {
        var credentials = await ReadCredentialsAsync(req);

        if (credentials is null
            || string.IsNullOrWhiteSpace(credentials.Username)
            || string.IsNullOrWhiteSpace(credentials.Password))
        {
            return await Unauthorized(req);
        }

        var username = credentials.Username.Trim().ToLowerInvariant();

        UserEntity? user = null;
        try
        {
            user = (await _tables.Users.GetEntityAsync<UserEntity>("user", username)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Handled below
        }

        if (user is null)
        {
            PasswordHasher.Verify(credentials.Password, Decoy.Hash, Decoy.Salt);
            return await Unauthorized(req);
        }

        if (!PasswordHasher.Verify(credentials.Password, user.PasswordHash, user.PasswordSalt))
        {
            _log.LogWarning("Failed login for {Username}", username);
            return await Unauthorized(req);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new LoginResponse(_tokens.Issue(username), username));
        return response;
    }


    private static Task<HttpResponseData> Unauthorized(HttpRequestData req) =>
        Responses.ErrorAsync(req, HttpStatusCode.Unauthorized, "Invalid username or password.");

    private static async Task<CredentialsRequest?> ReadCredentialsAsync(HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            return JsonSerializer.Deserialize<CredentialsRequest>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}