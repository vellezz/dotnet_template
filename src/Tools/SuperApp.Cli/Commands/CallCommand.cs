using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SuperApp.Cli.LocalEnvironment;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp call &lt;component&gt; &lt;path&gt; [options]</c>: invokes an API or BFF with automatic JWT authentication.</summary>
/// <remarks>
/// Resolves the component address from local endpoints, automatically obtains an access token from Keycloak
/// (configured with appropriate audience, scopes, and issuer), sends the HTTP request, and prints the formatted response.
/// </remarks>
internal static class CallCommand
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    /// <summary>Creates the <c>call</c> command.</summary>
    /// <param name="common">Common options.</param>
    /// <returns>The command.</returns>
    public static Command Create(CommonOptions common)
    {
        var component = new Argument<string>("component") { Description = "Target component, e.g. example-bff, knowledge-api, sleepdiary-api, bff-web, docs." };
        var path = new Argument<string>("path") { Description = "Endpoint path, e.g. /v1/materials or /internal/v1/widgets/sleep-summary." };
        var method = new Option<string>("--method", "-X") { Description = "HTTP method (GET, POST, PUT, DELETE, PATCH).", DefaultValueFactory = _ => "GET" };
        var data = new Option<string?>("--data", "-d") { Description = "HTTP request body (JSON string or @filename)." };
        var asUser = new Option<string>("--as") { Description = "User to authenticate as: editor or reader (default: editor).", DefaultValueFactory = _ => "editor" };
        var anonymous = new Option<bool>("--anonymous") { Description = "Send request without an Authorization header." };
        var k8s = new Option<bool>("--k8s") { Description = "Target local Kubernetes cluster (uses K8s token issuer and port mappings)." };
        var token = new Option<string?>("--token") { Description = "Explicit Bearer token to use instead of obtaining one automatically." };
        var headers = new Option<string[]>("--header", "-H") { Description = "Additional HTTP header in 'Name: Value' format.", AllowMultipleArgumentsPerToken = true };
        var verbose = new Option<bool>("--verbose", "-v") { Description = "Print response headers and timing details." };

        var command = new Command("call", "Invoke a service or BFF endpoint with automatic JWT token acquisition and formatted output.")
        {
            component,
            path,
            method,
            data,
            asUser,
            anonymous,
            k8s,
            token,
            headers,
            verbose,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var model = RepositoryScanner.Scan(root);
            var endpoints = new LocalEndpoints(model);
            var targetComponent = parseResult.GetValue(component)!;
            var targetPath = parseResult.GetValue(path)!;
            if (!targetPath.StartsWith('/'))
            {
                targetPath = "/" + targetPath;
            }

            var baseAddress = ResolveBaseAddress(endpoints, targetComponent);
            if (baseAddress is null)
            {
                output.Error($"Unknown component '{targetComponent}'. Available components: bff-web, gateway-mobile, docs, " +
                             $"{string.Join(", ", endpoints.Bffs.Select(b => $"{b.Experience}-bff"))}, " +
                             $"{string.Join(", ", endpoints.Services.Select(s => $"{s.Service}-api"))}.");
                return ExitCodes.NotFound;
            }

            var isK8s = parseResult.GetValue(k8s);
            var isAnonymous = parseResult.GetValue(anonymous);
            string? authToken = parseResult.GetValue(token);

            using var http = new LocalHttp();

            if (!isAnonymous && string.IsNullOrWhiteSpace(authToken))
            {
                if (endpoints.TokenEndpoint is not { } tokenEndpoint)
                {
                    output.Error("Keycloak service is not configured in local environment.");
                    return ExitCodes.NotFound;
                }

                var user = parseResult.GetValue(asUser) ?? "editor";
                var scopes = string.Join(' ', ["openid", .. EnvCommand.AllScopes(model)]);
                var hostHeader = isK8s ? "keycloak:8080" : null;

                try
                {
                    var (issuedToken, _) = await http.TokenAsync(tokenEndpoint, user, user, scopes, hostHeader, cancellationToken);
                    authToken = issuedToken;
                }
                catch (InvalidOperationException exception)
                {
                    output.Error($"Failed to acquire token from Keycloak: {exception.Message}");
                    return ExitCodes.Failed;
                }
            }

            var requestUrl = $"{baseAddress.TrimEnd('/')}{targetPath}";
            var httpMethod = new HttpMethod(parseResult.GetValue(method)!.ToUpperInvariant());
            using var request = new HttpRequestMessage(httpMethod, requestUrl);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            }

            if (parseResult.GetValue(headers) is { Length: > 0 } customHeaders)
            {
                foreach (var header in customHeaders)
                {
                    var separator = header.IndexOf(':');
                    if (separator > 0)
                    {
                        var headerName = header[..separator].Trim();
                        var headerVal = header[(separator + 1)..].Trim();
                        request.Headers.TryAddWithoutValidation(headerName, headerVal);
                    }
                }
            }

            var bodyText = parseResult.GetValue(data);
            if (!string.IsNullOrWhiteSpace(bodyText))
            {
                if (bodyText.StartsWith('@') && File.Exists(Path.Combine(root, bodyText[1..])))
                {
                    bodyText = await File.ReadAllTextAsync(Path.Combine(root, bodyText[1..]), cancellationToken);
                }

                request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");
            }

            var stopwatch = Stopwatch.StartNew();
            using var client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (Exception exception)
            {
                output.Error($"Request to {requestUrl} failed: {exception.Message}");
                return ExitCodes.Failed;
            }

            stopwatch.Stop();
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var isSuccess = response.IsSuccessStatusCode;

            if (output.IsJson)
            {
                object parsedBody;
                try
                {
                    parsedBody = JsonDocument.Parse(responseContent).RootElement.Clone();
                }
                catch
                {
                    parsedBody = responseContent;
                }

                output.WriteJson(new
                {
                    statusCode = (int)response.StatusCode,
                    durationMs = stopwatch.ElapsedMilliseconds,
                    url = requestUrl,
                    body = parsedBody,
                });
                return isSuccess ? ExitCodes.Success : ExitCodes.FindingsFound;
            }

            if (parseResult.GetValue(verbose))
            {
                output.Line($"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase} ({stopwatch.ElapsedMilliseconds} ms)");
                output.Line($"URL: {requestUrl}");
                foreach (var (headerName, headerVals) in response.Headers)
                {
                    output.Line($"{headerName}: {string.Join(", ", headerVals)}");
                }

                output.Line();
            }
            else
            {
                output.Line($"[{(int)response.StatusCode} {response.ReasonPhrase}] in {stopwatch.ElapsedMilliseconds}ms -> {requestUrl}");
            }

            if (!string.IsNullOrWhiteSpace(responseContent))
            {
                try
                {
                    using var doc = JsonDocument.Parse(responseContent);
                    output.Line(JsonSerializer.Serialize(doc.RootElement, PrettyJsonOptions));
                }
                catch
                {
                    output.Line(responseContent);
                }
            }

            return isSuccess ? ExitCodes.Success : ExitCodes.FindingsFound;
        });

        return command;
    }

    private static string? ResolveBaseAddress(LocalEndpoints endpoints, string component)
    {
        var normalized = component.ToLowerInvariant().Replace("-api", "").Replace("-bff", "");

        if (normalized is "docs" or "superapp-docs")
        {
            return "http://localhost:8088";
        }

        if (normalized is "keycloak")
        {
            return endpoints.Keycloak ?? "http://localhost:8081";
        }

        if (normalized is "bff-web" or "web")
        {
            return endpoints.BffWeb ?? "https://localhost:5001";
        }

        if (normalized is "gateway-mobile" or "mobile")
        {
            return endpoints.GatewayMobile ?? "https://localhost:5002";
        }

        if (endpoints.Bffs.FirstOrDefault(b => b.Experience.Equals(normalized, StringComparison.OrdinalIgnoreCase)) is { } bff && bff.Address is not null)
        {
            return bff.Address;
        }

        if (endpoints.Services.FirstOrDefault(s => s.Service.Equals(normalized, StringComparison.OrdinalIgnoreCase)) is { } service && service.Address is not null)
        {
            return service.Address;
        }

        return null;
    }
}
