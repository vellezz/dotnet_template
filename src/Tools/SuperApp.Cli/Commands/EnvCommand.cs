using System.CommandLine;
using System.Diagnostics;
using SuperApp.Cli.LocalEnvironment;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp env up|down|status|token</c>: the local environment of docker compose (ADR-0034, chapter 13).</summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>up [--build] [--infra]</c>: starts the infrastructure and, without <c>--infra</c>, the application profile, then waits
///   until every component answers its health endpoint;</description></item>
///   <item><description><c>down [--reset]</c>: stops everything; <c>--reset</c> also deletes the volumes (database, realm state), so the next
///   <c>up</c> starts from scratch;</description></item>
///   <item><description><c>status</c>: containers with state and health, and the HTTP probes of every component (exit code 2 when something is
///   down);</description></item>
///   <item><description><c>token &lt;user&gt; [--scope …]</c>: an access token of a local user for curl or Postman.</description></item>
/// </list>
/// </remarks>
internal static class EnvCommand
{
    // Containers that run once and exit (bootstrap, migrations): exit code 0 is their success.
    private static readonly HashSet<string> OneShot = new(StringComparer.Ordinal) { "db-bootstrap", "migrator", "db-gateway-permissions" };

    /// <summary>Creates the command with its subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>env</c> command.</returns>
    public static Command Create(CommonOptions common) =>
        new("env", "The local environment (docker compose or Kubernetes): start, stop, check, get a token.")
        {
            Up(common),
            Down(common),
            Status(common),
            Token(common),
        };

    private static Command Up(CommonOptions common)
    {
        var build = new Option<bool>("--build") { Description = "Rebuild the images of the application before starting." };
        var infra = new Option<bool>("--infra") { Description = "Start only the infrastructure (MSSQL, RabbitMQ, Redis, Keycloak), e.g. to run services from the IDE." };
        var k8s = new Option<bool>("--k8s") { Description = "Start in local Kubernetes cluster (deploy/local/k8s and Helm) instead of docker compose." };
        var timeout = new Option<int>("--timeout") { Description = "Seconds to wait for the components (default 300).", DefaultValueFactory = _ => 300 };
        var command = new Command("up", "Start the local environment and wait until every component answers.") { build, infra, k8s, timeout };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var model = RepositoryScanner.Scan(root);
            if (parseResult.GetValue(k8s))
            {
                var k8sEnv = new KubernetesEnvironment(root, model);
                var upResult = k8sEnv.Up(parseResult.GetValue(infra), parseResult.GetValue(build), output);
                if (upResult != ExitCodes.Success)
                {
                    return upResult;
                }

                var pods = k8sEnv.Pods();
                KubernetesEnvironment.WritePods(output, pods);
                return pods.All(p => p.Ok) ? ExitCodes.Success : ExitCodes.FindingsFound;
            }

            var arguments = new List<string>();
            if (!parseResult.GetValue(infra))
            {
                arguments.AddRange(["--profile", "app"]);
            }

            arguments.AddRange(["up", "-d"]);
            if (parseResult.GetValue(build))
            {
                arguments.Add("--build");
            }

            if (Docker(output, () => new DockerCompose(root).Run([.. arguments])) is { } failure)
            {
                return failure;
            }

            var endpoints = new LocalEndpoints(model);
            var probes = parseResult.GetValue(infra) ? endpoints.Probes().Take(1).ToList() : endpoints.Probes();
            using var http = new LocalHttp();
            var watch = Stopwatch.StartNew();
            List<(string Name, string Url, int Status)> results;
            do
            {
                results = await ProbeAsync(http, probes, cancellationToken);
                if (results.All(result => result.Status == 200))
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            while (watch.Elapsed < TimeSpan.FromSeconds(parseResult.GetValue(timeout)));

            WriteProbes(output, results);
            return results.All(result => result.Status == 200) ? ExitCodes.Success : ExitCodes.FindingsFound;
        });
        return command;
    }

    private static Command Down(CommonOptions common)
    {
        var reset = new Option<bool>("--reset") { Description = "Also delete the volumes (database and its data); the next up starts from scratch." };
        var k8s = new Option<bool>("--k8s") { Description = "Stop local Kubernetes environment instead of docker compose." };
        var command = new Command("down", "Stop the local environment.") { reset, k8s };
        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            if (parseResult.GetValue(k8s))
            {
                return new KubernetesEnvironment(root, RepositoryScanner.Scan(root)).Down(parseResult.GetValue(reset), output);
            }

            string[] arguments = parseResult.GetValue(reset) ? ["--profile", "app", "down", "-v", "--remove-orphans"] : ["--profile", "app", "down", "--remove-orphans"];
            return Docker(output, () => new DockerCompose(root).Run(arguments)) ?? ExitCodes.Success;
        });
        return command;
    }

    private static Command Status(CommonOptions common)
    {
        var k8s = new Option<bool>("--k8s") { Description = "Show the status of local Kubernetes pods instead of docker compose." };
        var command = new Command("status", "Show the containers with state and health and probe every component over HTTP.") { k8s };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var model = RepositoryScanner.Scan(root);
            if (parseResult.GetValue(k8s))
            {
                var k8sEnv = new KubernetesEnvironment(root, model);
                var pods = k8sEnv.Pods();
                KubernetesEnvironment.WritePods(output, pods);
                return pods.All(p => p.Ok) ? ExitCodes.Success : ExitCodes.FindingsFound;
            }

            IReadOnlyList<(string Service, string State, string Health, int ExitCode)> containers;
            try
            {
                containers = new DockerCompose(root).Containers();
            }
            catch (InvalidOperationException exception)
            {
                output.Error(exception.Message);
                return ExitCodes.Failed;
            }

            using var http = new LocalHttp();
            var probes = await ProbeAsync(http, new LocalEndpoints(model).Probes(), cancellationToken);
            var problems = containers.Where(container => !IsOk(container)).Select(container => container.Service)
                .Concat(probes.Where(probe => probe.Status != 200).Select(probe => probe.Name)).ToList();
            if (output.IsJson)
            {
                output.WriteJson(new
                {
                    ok = problems.Count == 0,
                    containers = containers.Select(container => new { container.Service, container.State, container.Health, container.ExitCode, ok = IsOk(container) }),
                    probes = probes.Select(probe => new { probe.Name, probe.Url, probe.Status }),
                });
            }
            else
            {
                output.Table(["Container", "State", "Health", "Ok"],
                    containers.Select(container => (IReadOnlyList<string>)[container.Service, container.State == "exited" ? $"exited ({container.ExitCode})" : container.State, container.Health is { Length: > 0 } health ? health : "-", IsOk(container) ? "yes" : "NO"]));
                output.Line();
                WriteProbes(output, probes);
            }

            return problems.Count == 0 ? ExitCodes.Success : ExitCodes.FindingsFound;
        });
        return command;
    }

    private static Command Token(CommonOptions common)
    {
        var user = new Argument<string>("user")
        {
            Description = "Local user of the realm: editor or reader (ADR-0031).",
            DefaultValueFactory = _ => "editor",
        };
        var password = new Option<string?>("--password") { Description = "Password (default: the user name, as in the local realm)." };
        var k8s = new Option<bool>("--k8s") { Description = "Target local Kubernetes cluster (sets token issuer to http://keycloak:8080/realms/superapp)." };
        var issuer = new Option<string?>("--issuer") { Description = "Explicit authority / issuer URL override (e.g. http://keycloak:8080/realms/superapp)." };
        var scope = new Option<string[]>("--scope")
        {
            Description = "Scopes to request (default: every scope of the services and BFFs).",
            AllowMultipleArgumentsPerToken = true,
        };
        var audience = new Option<string[]>("--audience")
        {
            Description = "Expected audience(s) to verify in the token (e.g. example-bff, knowledge-api).",
            AllowMultipleArgumentsPerToken = true,
        };
        var curl = new Option<bool>("--curl") { Description = "Print formatted curl Authorization header (-H \"Authorization: Bearer <token>\")." };
        var envVar = new Option<bool>("--env-var") { Description = "Print export statement for shell: export TOKEN=\"<token>\"." };
        var decode = new Option<bool>("--decode") { Description = "Print decoded token payload details (issuer, subject, audiences, scopes, roles, expires)." };
        var command = new Command("token", "Print an access token of a local user, for curl: curl -H \"Authorization: Bearer $(dotnet superapp env token reader)\" ...")
        {
            user,
            password,
            k8s,
            issuer,
            scope,
            audience,
            curl,
            envVar,
            decode,
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
            if (endpoints.TokenEndpoint is not { } tokenEndpoint)
            {
                output.Error("Keycloak service is not configured in local environment.");
                return ExitCodes.NotFound;
            }

            var isK8s = parseResult.GetValue(k8s);
            var customIssuer = parseResult.GetValue(issuer);
            string? hostHeader = null;

            if (!string.IsNullOrWhiteSpace(customIssuer))
            {
                if (Uri.TryCreate(customIssuer, UriKind.Absolute, out var uri))
                {
                    hostHeader = uri.Authority;
                }
                else
                {
                    hostHeader = customIssuer;
                }
            }
            else if (isK8s)
            {
                // In Kubernetes pods, Authentication__Authority is http://keycloak:8080/realms/superapp.
                // Setting Host: keycloak:8080 causes Keycloak to issue token with iss: http://keycloak:8080/realms/superapp.
                hostHeader = "keycloak:8080";
            }

            var name = parseResult.GetValue(user) ?? "editor";
            var scopes = parseResult.GetValue(scope) is { Length: > 0 } requested ? requested : AllScopes(model);
            using var http = new LocalHttp();
            try
            {
                var (token, granted) = await http.TokenAsync(
                    tokenEndpoint,
                    name,
                    parseResult.GetValue(password) ?? name,
                    string.Join(' ', ["openid", .. scopes]),
                    hostHeader: hostHeader,
                    cancellationToken: cancellationToken);

                var decoded = JwtTokenDecoder.Decode(token);

                if (parseResult.GetValue(audience) is { Length: > 0 } expectedAudiences)
                {
                    var missing = expectedAudiences.Where(expected => !decoded.Audiences.Contains(expected, StringComparer.OrdinalIgnoreCase)).ToList();
                    if (missing.Count > 0)
                    {
                        output.Line($"warning: Token is missing requested audience(s): {string.Join(", ", missing)}");
                    }
                }

                if (output.IsJson)
                {
                    output.WriteJson(new
                    {
                        accessToken = token,
                        tokenType = "Bearer",
                        scope = granted,
                        issuer = decoded.Issuer,
                        subject = decoded.Subject,
                        username = decoded.Username,
                        audiences = decoded.Audiences,
                        scopes = decoded.Scopes,
                        roles = decoded.Roles,
                        expiresAt = decoded.ExpiresAt,
                    });
                }
                else if (parseResult.GetValue(curl))
                {
                    output.Line($"-H \"Authorization: Bearer {token}\"");
                }
                else if (parseResult.GetValue(envVar))
                {
                    output.Line($"export TOKEN=\"{token}\"");
                }
                else
                {
                    output.Line(token);

                    if (parseResult.GetValue(decode))
                    {
                        output.Line();
                        output.Table(
                            ["Claim", "Value"],
                            [
                                ["Issuer", decoded.Issuer],
                                ["Subject", decoded.Subject],
                                ["User", decoded.Username],
                                ["Audiences", string.Join(", ", decoded.Audiences)],
                                ["Scopes", string.Join(", ", decoded.Scopes)],
                                ["Roles", string.Join(", ", decoded.Roles)],
                                ["Expires", decoded.ExpiresAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture)]
                            ]);
                    }
                }

                return ExitCodes.Success;
            }
            catch (InvalidOperationException exception)
            {
                output.Error(exception.Message);
                return ExitCodes.Failed;
            }
        });
        return command;
    }

    /// <summary>Every scope declared by the services and BFFs; the realm grants only those the user may have.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <returns>The scopes.</returns>
    public static string[] AllScopes(RepositoryModel model) =>
        [.. model.Services.SelectMany(service => service.Scopes).Concat(model.Bffs.SelectMany(bff => bff.Scopes)).Distinct(StringComparer.Ordinal)];

    private static bool IsOk((string Service, string State, string Health, int ExitCode) container) =>
        OneShot.Contains(container.Service)
            ? container.State == "exited" && container.ExitCode == 0
            : container.State == "running" && container.Health is not "unhealthy" and not "starting";

    private static async Task<List<(string Name, string Url, int Status)>> ProbeAsync(LocalHttp http, IReadOnlyList<(string Name, string Url)> probes, CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(probes.Select(async probe => (probe.Name, probe.Url, (await http.SendAsync(HttpMethod.Get, probe.Url, cancellationToken: cancellationToken)).Status)));
        return [.. results];
    }

    private static void WriteProbes(OutputWriter output, List<(string Name, string Url, int Status)> probes)
    {
        if (output.IsJson)
        {
            output.WriteJson(probes.Select(probe => new { probe.Name, probe.Url, probe.Status, ok = probe.Status == 200 }));
            return;
        }

        output.Table(["Component", "Probe", "Status"],
            probes.Select(probe => (IReadOnlyList<string>)[probe.Name, probe.Url, probe.Status == 0 ? "unreachable" : probe.Status.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
    }

    private static int? Docker(OutputWriter output, Func<int> run)
    {
        try
        {
            return run() == 0 ? null : ExitCodes.Failed;
        }
        catch (InvalidOperationException exception)
        {
            output.Error(exception.Message);
            return ExitCodes.Failed;
        }
    }
}
