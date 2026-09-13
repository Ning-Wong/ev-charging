using ChargingFunctions.Auth;

namespace CarSimulator.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Accepts_the_correct_password()
    {
        var (hash, salt) = PasswordHasher.Create("correct horse battery staple");

        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash, salt));
    }

    [Fact]
    public void Rejects_a_wrong_password()
    {
        var (hash, salt) = PasswordHasher.Create("correct horse battery staple");

        Assert.False(PasswordHasher.Verify("Correct horse battery staple", hash, salt));
    }

    [Fact]
    public void Same_password_produces_different_hashes()
    {
        var first = PasswordHasher.Create("same password");
        var second = PasswordHasher.Create("same password");

        Assert.NotEqual(first.Hash, second.Hash);
        Assert.NotEqual(first.Salt, second.Salt);
    }

    [Fact]
    public void Rejects_a_corrupted_record_without_throwing()
    {
        Assert.False(PasswordHasher.Verify("anything", "not base64!", "nor is this!"));
    }
}

public class TokenServiceTests
{
    private const string Secret = "a-test-signing-key-long-enough-for-hmac-sha256";

    [Fact]
    public void Round_trips_the_user_id()
    {
        var tokens = new TokenService(Secret);

        var token = tokens.Issue("compac");

        Assert.Equal("compac", tokens.ValidateAndGetUserId($"Bearer {token}"));
    }

    [Fact]
    public void Rejects_a_token_signed_with_another_key()
    {
        var issued = new TokenService(Secret).Issue("compac");
        var other = new TokenService("a-completely-different-key-of-sufficient-length");

        Assert.Null(other.ValidateAndGetUserId($"Bearer {issued}"));
    }

    [Fact]
    public void Rejects_a_tampered_payload()
    {
        var tokens = new TokenService(Secret);
        var token = tokens.Issue("compac");

        var parts = token.Split('.');
        parts[1] = parts[1][..^1] + (parts[1][^1] == 'A' ? 'B' : 'A');

        Assert.Null(tokens.ValidateAndGetUserId($"Bearer {string.Join('.', parts)}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-bearer-token")]
    [InlineData("Bearer ")]
    [InlineData("Bearer garbage")]
    public void Rejects_malformed_headers(string? header)
    {
        Assert.Null(new TokenService(Secret).ValidateAndGetUserId(header));
    }

    [Fact]
    public void Refuses_a_signing_key_that_is_too_short()
    {
        Assert.Throws<InvalidOperationException>(() => new TokenService("too short"));
    }
}