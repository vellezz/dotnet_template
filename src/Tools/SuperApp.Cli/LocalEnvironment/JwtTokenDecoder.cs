using System.Text.Json;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>Decodes and inspects JSON Web Tokens (JWT) without external dependencies.</summary>
internal static class JwtTokenDecoder
{
    /// <summary>Decodes a raw JWT into its main OIDC claims.</summary>
    /// <param name="token">Raw JWT string (three dot-separated segments).</param>
    /// <returns>Decoded claims representation.</returns>
    /// <exception cref="InvalidOperationException">The token is not a valid three-part JWT.</exception>
    public static DecodedToken Decode(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException("Invalid JWT format: expected at least two segments.");
        }

        var payloadBytes = Base64UrlDecode(parts[1]);
        using var document = JsonDocument.Parse(payloadBytes);
        var root = document.RootElement;

        var issuer = root.TryGetProperty("iss", out var iss) ? iss.GetString() ?? string.Empty : string.Empty;
        var subject = root.TryGetProperty("sub", out var sub) ? sub.GetString() ?? string.Empty : string.Empty;
        var username = root.TryGetProperty("preferred_username", out var pu) ? pu.GetString() ?? string.Empty
            : root.TryGetProperty("username", out var u) ? u.GetString() ?? string.Empty : string.Empty;

        var audiences = new List<string>();
        if (root.TryGetProperty("aud", out var aud))
        {
            if (aud.ValueKind == JsonValueKind.Array)
            {
                audiences.AddRange(aud.EnumerateArray().Select(item => item.GetString()!).Where(item => item is not null));
            }
            else if (aud.ValueKind == JsonValueKind.String && aud.GetString() is { } singleAud)
            {
                audiences.Add(singleAud);
            }
        }

        var scopes = root.TryGetProperty("scope", out var sc) && sc.GetString() is { Length: > 0 } scopeStr
            ? scopeStr.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : [];

        var roles = new List<string>();
        if (root.TryGetProperty("realm_access", out var ra) && ra.TryGetProperty("roles", out var rArray) && rArray.ValueKind == JsonValueKind.Array)
        {
            roles.AddRange(rArray.EnumerateArray().Select(item => item.GetString()!).Where(item => item is not null));
        }

        DateTimeOffset expiresAt = default;
        if (root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var expUnix))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix);
        }

        return new DecodedToken(token, issuer, subject, username, audiences, scopes, roles, expiresAt);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }

        return Convert.FromBase64String(output);
    }
}
