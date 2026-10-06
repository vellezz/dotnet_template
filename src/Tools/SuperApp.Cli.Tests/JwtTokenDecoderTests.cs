using System.Text;
using System.Text.Json;
using SuperApp.Cli.LocalEnvironment;

namespace SuperApp.Cli.Tests;

public sealed class JwtTokenDecoderTests
{
    [Fact]
    public void Decodes_valid_jwt_payload_with_audiences_roles_and_scopes()
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payloadObj = new
        {
            iss = "http://keycloak:8080/realms/superapp",
            sub = "12345-67890",
            preferred_username = "testuser",
            aud = new[] { "example-bff", "knowledge-api" },
            scope = "openid knowledge.catalog.read example.internal.read",
            realm_access = new { roles = new[] { "knowledge-editor", "admin" } },
            exp = 1791317574,
        };
        var payloadJson = JsonSerializer.Serialize(payloadObj);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"{header}.{payload}.dummySignature";

        var decoded = JwtTokenDecoder.Decode(token);

        Assert.Equal("http://keycloak:8080/realms/superapp", decoded.Issuer);
        Assert.Equal("12345-67890", decoded.Subject);
        Assert.Equal("testuser", decoded.Username);
        Assert.Equal(["example-bff", "knowledge-api"], decoded.Audiences);
        Assert.Equal(["openid", "knowledge.catalog.read", "example.internal.read"], decoded.Scopes);
        Assert.Equal(["knowledge-editor", "admin"], decoded.Roles);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791317574), decoded.ExpiresAt);
    }

    [Fact]
    public void Decodes_single_string_audience_as_list()
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payloadJson = JsonSerializer.Serialize(new { aud = "single-service-api" });
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"{header}.{payload}.dummy";

        var decoded = JwtTokenDecoder.Decode(token);

        Assert.Single(decoded.Audiences, "single-service-api");
    }

    [Fact]
    public void Throws_for_invalid_token_format()
    {
        Assert.Throws<InvalidOperationException>(() => JwtTokenDecoder.Decode("invalidTokenWithoutDots"));
    }
}
