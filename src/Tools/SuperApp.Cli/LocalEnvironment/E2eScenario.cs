using System.Globalization;
using System.Text.Json;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>
/// The end-to-end scenario of <c>dotnet superapp e2e</c>: requests through the running local environment that check the contracts between
/// the components, not the business logic (that is covered by the tests of each service).
/// </summary>
/// <remarks>
/// <para>For every BFF found in the repository:</para>
/// <list type="bullet">
///   <item><description>the edge gateway (<c>gateway-mobile</c>) routes <c>/api/{experience}/v1/…</c> with a user token and answers
///   <c>401 auth.invalid_token</c> without one (ADR-0044);</description></item>
///   <item><description>the internal API is never routed by the gateway (ADR-0039) and, called on the BFF, requires the scope
///   <c>{experience}.internal.read</c> (<c>403 auth.missing_scope</c> without it, ADR-0040);</description></item>
///   <item><description>an unknown path of the BFF answers <c>404 http.not_found</c> with a 32-hex <c>traceId</c>.</description></item>
/// </list>
/// <para>
/// For a BFF with a SleepDiary client (the example experience) also a full write path through the gateway: record a diary entry for a
/// random past day, read it, delete it. Finally the full browser login to <c>bff-web</c> (<see cref="E2eBffWebLogin"/>). Data and sessions
/// created by the scenario are removed again.
/// </para>
/// </remarks>
internal static class E2eScenario
{
    /// <summary>Runs the scenario.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="http">HTTP access to the local environment.</param>
    /// <param name="cancellationToken">Cancels the scenario.</param>
    /// <returns>The checks in the order they ran.</returns>
    public static async Task<IReadOnlyList<E2eCheck>> RunAsync(RepositoryModel model, LocalHttp http, CancellationToken cancellationToken)
    {
        var endpoints = new LocalEndpoints(model);
        var checks = new List<E2eCheck>();
        void Check(string name, bool ok, (int Status, string Body) response) =>
            checks.Add(new E2eCheck(name, ok, $"{response.Status} {Shorten(response.Body)}"));

        foreach (var (name, url) in endpoints.Probes())
        {
            var health = await http.SendAsync(HttpMethod.Get, url, cancellationToken: cancellationToken);
            Check($"{name} answers its health endpoint", health.Status == 200, health);
        }

        if (endpoints.TokenEndpoint is not { } tokenEndpoint || endpoints.GatewayMobile is not { } gateway)
        {
            checks.Add(new E2eCheck("keycloak and gateway-mobile are in docker-compose.yml", false, "missing"));
            return checks;
        }

        var scopes = model.Services.SelectMany(service => service.Scopes).ToList();
        string userToken;
        string internalToken;
        try
        {
            userToken = (await http.TokenAsync(tokenEndpoint, "reader", "reader", string.Join(' ', ["openid", .. scopes]), cancellationToken)).Token;
            internalToken = (await http.TokenAsync(tokenEndpoint, "reader", "reader",
                string.Join(' ', ["openid", .. scopes, .. model.Bffs.SelectMany(bff => bff.Scopes)]), cancellationToken)).Token;
            checks.Add(new E2eCheck("local realm issues tokens to the user reader (dev-cli)", true, "200"));
        }
        catch (InvalidOperationException exception)
        {
            checks.Add(new E2eCheck("local realm issues tokens to the user reader (dev-cli)", false, exception.Message));
            return checks;
        }

        foreach (var (experience, bff) in endpoints.Bffs)
        {
            var info = model.Bffs.Single(candidate => candidate.Key == experience);
            var unknown = await http.SendAsync(HttpMethod.Get, $"{gateway}/api/{experience}/v1/e2e-unknown-path", userToken, cancellationToken: cancellationToken);
            Check($"gateway-mobile routes /api/{experience}/v1/** to {experience}-bff (unknown path: 404 http.not_found, 32-hex traceId)",
                unknown.Status == 404 && Code(unknown.Body) == "http.not_found" && TraceIdIsHex(unknown.Body), unknown);

            var anonymous = await http.SendAsync(HttpMethod.Get, $"{gateway}/api/{experience}/v1/e2e-unknown-path", cancellationToken: cancellationToken);
            Check($"gateway-mobile without a token: 401 auth.invalid_token", anonymous.Status == 401 && Code(anonymous.Body) == "auth.invalid_token", anonymous);

            var routedInternal = await http.SendAsync(HttpMethod.Get, $"{gateway}/api/{experience}/internal/v1/e2e", internalToken, cancellationToken: cancellationToken);
            Check($"gateway-mobile never routes the internal API of {experience}-bff", routedInternal.Status == 404, routedInternal);

            if (info.Scopes.Count > 0 && info.Clients.Contains("SleepDiary"))
            {
                const string Widget = "internal/v1/widgets/sleep-summary";
                var allowed = await http.SendAsync(HttpMethod.Get, $"{bff}/{Widget}", internalToken, cancellationToken: cancellationToken);
                Check($"{experience}-bff internal API with {info.Scopes[0]}: 200", allowed.Status == 200, allowed);
                var denied = await http.SendAsync(HttpMethod.Get, $"{bff}/{Widget}", userToken, cancellationToken: cancellationToken);
                Check($"{experience}-bff internal API without {info.Scopes[0]}: 403 auth.missing_scope", denied.Status == 403 && Code(denied.Body) == "auth.missing_scope", denied);

                await SleepEntryRoundTripAsync(http, $"{gateway}/api/{experience}/v1/sleepdiary/entries", userToken, Check, cancellationToken);
            }
        }

        if (endpoints.BffWeb is { } web)
        {
            // The SPA calls the API of the first experience; Knowledge categories are readable by reader, otherwise an unknown path of the BFF.
            var spa = model.Bffs.OrderBy(bff => bff.Key, StringComparer.Ordinal).FirstOrDefault();
            var (apiPath, apiStatus) = spa is null ? ("/api/e2e/v1/e2e-unknown-path", 404)
                : spa.Clients.Contains("Knowledge") ? ($"/api/{spa.Key}/v1/knowledge/categories", 200)
                : ($"/api/{spa.Key}/v1/e2e-unknown-path", 404);
            await E2eBffWebLogin.RunAsync(web, apiPath, apiStatus, (name, ok, detail) => checks.Add(new E2eCheck(name, ok, detail)), cancellationToken);
        }

        return checks;
    }

    // Record, read and delete a diary entry of a random past day through the gateway; the entry is removed again.
    private static async Task SleepEntryRoundTripAsync(LocalHttp http, string entries, string token, Action<string, bool, (int, string)> check, CancellationToken cancellationToken)
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(Random.Shared.Next(400, 4000)));
        var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var body = new
        {
            bedTime = $"{day.AddDays(-1):yyyy-MM-dd}T23:00:00",
            wakeTime = $"{date}T07:00:00",
            sleepLatencyMinutes = 15,
            awakenings = 1,
            quality = 4,
            notes = "dotnet superapp e2e",
        };

        var created = await http.SendAsync(HttpMethod.Post, $"{entries}/{date}", token, body, cancellationToken);
        check($"sleep diary through the gateway: record an entry for {date} (201)", created.Item1 == 201, created);
        var read = await http.SendAsync(HttpMethod.Get, $"{entries}/{date}", token, cancellationToken: cancellationToken);
        check("sleep diary: read the entry back (200)", read.Item1 == 200 && read.Item2.Contains("dotnet superapp e2e", StringComparison.Ordinal), read);
        var deleted = await http.SendAsync(HttpMethod.Delete, $"{entries}/{date}", token, cancellationToken: cancellationToken);
        check("sleep diary: delete the entry (204)", deleted.Item1 == 204, deleted);
        var gone = await http.SendAsync(HttpMethod.Get, $"{entries}/{date}", token, cancellationToken: cancellationToken);
        check("sleep diary: the deleted entry is gone (404 sleepdiary.entry.not_found)", gone.Item1 == 404 && Code(gone.Item2) == "sleepdiary.entry.not_found", gone);
    }

    private static string? Code(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TraceIdIsHex(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("traceId", out var trace) && trace.GetString() is { Length: 32 } id && id.All(Uri.IsHexDigit);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Shorten(string text) => text.Length <= 120 ? text.ReplaceLineEndings(" ") : text[..120].ReplaceLineEndings(" ") + "…";
}
