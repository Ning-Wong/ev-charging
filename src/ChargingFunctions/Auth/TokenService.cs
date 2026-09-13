using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ChargingFunctions.Auth;

// Issues and validates the session token. The token carries the user id and an expiry and is signed.
public class TokenService
{
    private const string Issuer = "ev-charging";
    private const string Audience = "ev-charging-api";

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly SymmetricSecurityKey _key;

    public TokenService(string signingSecret)
    {
        if (signingSecret.Length < 32)
            throw new InvalidOperationException(
                "JwtSigningKey must be at least 32 characters for HMAC-SHA256.");

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingSecret));
    }

    public string Issue(string userId)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId)],
            expires: DateTime.UtcNow.Add(Lifetime),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string? ValidateAndGetUserId(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var raw = authorizationHeader["Bearer ".Length..].Trim();

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(raw,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _key,
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                },
                out _);

            return principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                   ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}