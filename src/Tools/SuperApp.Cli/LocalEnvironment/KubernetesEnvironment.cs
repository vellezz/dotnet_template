using System.Diagnostics;
using System.Text.Json;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>Manages the local Kubernetes environment (Docker Desktop, Minikube, Kind) for the SuperApp (ADR-0034).</summary>
/// <remarks>
/// Provides parity with <see cref="DockerCompose"/>: starts/stops local infrastructure manifests (<c>deploy/local/k8s</c>),
/// runs migrations, deploys application Helm charts, and inspects pod statuses.
/// </remarks>
/// <param name="root">Repository root directory.</param>
/// <param name="model">Repository model containing registered services and BFFs.</param>
internal sealed class KubernetesEnvironment(string root, RepositoryModel model)
{
    private const string ManifestsDir = "deploy/local/k8s";
    private const string HelmDir = "deploy/helm";

    /// <summary>Checks whether the local Kubernetes cluster is reachable via <c>kubectl</c>.</summary>
    /// <returns><see langword="true"/> if reachable; otherwise, <see langword="false"/>.</returns>
    public bool IsClusterReachable()
    {
        try
        {
            var start = new ProcessStartInfo("kubectl")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ArgumentList = { "cluster-info" },
            };
            using var process = Process.Start(start);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Brings up the local Kubernetes environment (infrastructure manifests and optional Helm application charts).</summary>
    /// <param name="infraOnly">When <see langword="true"/>, starts only infrastructure (MSSQL, Redis, RabbitMQ, Keycloak).</param>
    /// <param name="build">Whether to rebuild container images locally before deploying.</param>
    /// <param name="output">Output writer.</param>
    /// <returns>Exit code (0 on success).</returns>
    public int Up(bool infraOnly, bool build, OutputWriter output)
    {
        if (!IsClusterReachable())
        {
            output.Error("Kubernetes cluster is not reachable. Ensure Docker Desktop Kubernetes, Minikube, or Kind is running.");
            return ExitCodes.Failed;
        }

        output.Line("==> Applying local Kubernetes infrastructure manifests (deploy/local/k8s)...");
        string[] infraFiles =
        [
            Path.Combine(root, ManifestsDir, "01-mssql.yaml"),
            Path.Combine(root, ManifestsDir, "02-redis.yaml"),
            Path.Combine(root, ManifestsDir, "03-rabbitmq.yaml"),
            Path.Combine(root, ManifestsDir, "04-keycloak.yaml"),
            Path.Combine(root, ManifestsDir, "05-secrets.yaml"),
        ];

        var applyArgs = new List<string> { "apply" };
        foreach (var file in infraFiles)
        {
            if (File.Exists(file))
            {
                applyArgs.AddRange(["-f", file]);
            }
        }

        if (RunKubectl(output, [.. applyArgs]) != 0)
        {
            output.Error("Failed to apply infrastructure manifests.");
            return ExitCodes.Failed;
        }

        output.Line("==> Waiting for infrastructure pods to become ready...");
        RunKubectl(output, "wait", "--for=condition=ready", "pod", "-l", "app.kubernetes.io/name=mssql", "--timeout=120s");
        RunKubectl(output, "wait", "--for=condition=ready", "pod", "-l", "app.kubernetes.io/name=redis", "--timeout=60s");
        RunKubectl(output, "wait", "--for=condition=ready", "pod", "-l", "app.kubernetes.io/name=rabbitmq", "--timeout=60s");
        RunKubectl(output, "wait", "--for=condition=ready", "pod", "-l", "app.kubernetes.io/name=keycloak", "--timeout=120s");

        var migratorJobPath = Path.Combine(root, ManifestsDir, "06-migrator-job.yaml");
        if (File.Exists(migratorJobPath))
        {
            output.Line("==> Running database migrations and permissions job...");
            RunKubectl(output, "delete", "job", "superapp-migrator", "gateway-permissions", "--ignore-not-found");
            RunKubectl(output, "apply", "-f", migratorJobPath);
            RunKubectl(output, "wait", "--for=condition=complete", "job/gateway-permissions", "--timeout=120s");
        }

        if (infraOnly)
        {
            output.Line("Infrastructure is up and ready. Skipping application deployments (--infra).");
            return ExitCodes.Success;
        }

        output.Line("==> Deploying application workloads via Helm...");

        // Deploy domain services
        var serviceChartPath = Path.Combine(root, HelmDir, "superapp-service");
        foreach (var service in model.Services)
        {
            var valuesFile = Path.Combine(serviceChartPath, $"values-{service.Key}.yaml");
            var args = new List<string>
            {
                "upgrade", "--install", service.Key, serviceChartPath,
                "--set", "authentication.authority=http://keycloak:8080/realms/superapp",
                "--set", "image.registry=library",
                "--set", "image.tag=latest",
                "--set", "image.pullPolicy=IfNotPresent",
            };
            if (File.Exists(valuesFile))
            {
                args.AddRange(["-f", valuesFile]);
            }

            RunHelm(output, [.. args]);
        }

        // Deploy BFFs
        var bffChartPath = Path.Combine(root, HelmDir, "superapp-bff");
        foreach (var bff in model.Bffs)
        {
            var valuesFile = Path.Combine(bffChartPath, $"values-{bff.Key}.yaml");
            var args = new List<string>
            {
                "upgrade", "--install", $"{bff.Key}-bff", bffChartPath,
                "--set", "authentication.authority=http://keycloak:8080/realms/superapp",
                "--set", "image.registry=library",
                "--set", "image.tag=latest",
                "--set", "image.pullPolicy=IfNotPresent",
            };
            if (File.Exists(valuesFile))
            {
                args.AddRange(["-f", valuesFile]);
            }

            RunHelm(output, [.. args]);
        }

        // Deploy Gateway (bff-web)
        var gatewayChartPath = Path.Combine(root, HelmDir, "superapp-gateway");
        var bffWebValues = Path.Combine(gatewayChartPath, "values-bff-web.yaml");
        var gwArgs = new List<string>
        {
            "upgrade", "--install", "bff-web", gatewayChartPath,
            "--set", "authentication.authority=http://keycloak:8080/realms/superapp",
            "--set", "image.registry=library",
            "--set", "image.tag=latest",
            "--set", "image.pullPolicy=IfNotPresent",
        };
        if (File.Exists(bffWebValues))
        {
            gwArgs.AddRange(["-f", bffWebValues]);
        }

        RunHelm(output, [.. gwArgs]);

        // Deploy Analytics Forwarder
        var analyticsChartPath = Path.Combine(root, HelmDir, "superapp-analytics-forwarder");
        if (Directory.Exists(analyticsChartPath))
        {
            RunHelm(output, "upgrade", "--install", "analytics", analyticsChartPath,
                "--set", "image.registry=library",
                "--set", "image.tag=latest",
                "--set", "image.pullPolicy=IfNotPresent");
        }

        // Deploy Documentation Portal
        var docsChartPath = Path.Combine(root, HelmDir, "superapp-docs");
        if (Directory.Exists(docsChartPath))
        {
            RunHelm(output, "upgrade", "--install", "superapp-docs", docsChartPath);
        }

        output.Line("==> All application releases deployed successfully.");
        return ExitCodes.Success;
    }

    /// <summary>Stops and tears down the local Kubernetes environment.</summary>
    /// <param name="reset">When <see langword="true"/>, also deletes PVCs and Jobs.</param>
    /// <param name="output">Output writer.</param>
    /// <returns>Exit code (0 on success).</returns>
    public int Down(bool reset, OutputWriter output)
    {
        output.Line("==> Uninstalling Helm application releases...");
        var releases = new List<string> { "superapp-docs", "bff-web", "analytics" };
        releases.AddRange(model.Bffs.Select(bff => $"{bff.Key}-bff"));
        releases.AddRange(model.Services.Select(svc => svc.Key));

        foreach (var release in releases)
        {
            RunHelm(output, "uninstall", release, "--ignore-not-found");
        }

        output.Line("==> Deleting local Kubernetes infrastructure manifests...");
        var localK8sDir = Path.Combine(root, ManifestsDir);
        if (Directory.Exists(localK8sDir))
        {
            RunKubectl(output, "delete", "-f", localK8sDir, "--ignore-not-found");
        }

        if (reset)
        {
            output.Line("==> Resetting persistent volume claims and jobs (--reset)...");
            RunKubectl(output, "delete", "pvc", "--all", "--ignore-not-found");
            RunKubectl(output, "delete", "jobs", "--all", "--ignore-not-found");
        }

        output.Line("Local Kubernetes environment stopped.");
        return ExitCodes.Success;
    }

    /// <summary>Retrieves all pods in the default namespace with their status, readiness, restarts and age.</summary>
    /// <returns>List of pod details.</returns>
    public IReadOnlyList<(string Name, string Status, string Ready, int Restarts, string Age, bool Ok)> Pods()
    {
        string json;
        try
        {
            json = ReadKubectl("get", "pods", "-o", "json");
        }
        catch
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items))
            {
                return [];
            }

            var list = new List<(string Name, string Status, string Ready, int Restarts, string Age, bool Ok)>();
            foreach (var item in items.EnumerateArray())
            {
                var name = item.GetProperty("metadata").GetProperty("name").GetString() ?? "?";
                var statusObj = item.GetProperty("status");
                var phase = statusObj.TryGetProperty("phase", out var p) ? p.GetString() ?? "?" : "?";

                var containerStatuses = statusObj.TryGetProperty("containerStatuses", out var cs) && cs.ValueKind == JsonValueKind.Array
                    ? cs.EnumerateArray().ToList()
                    : [];

                var readyCount = containerStatuses.Count(c => c.TryGetProperty("ready", out var r) && r.GetBoolean());
                var totalContainers = containerStatuses.Count;
                var readyStr = $"{readyCount}/{totalContainers}";
                var restarts = containerStatuses.Sum(c => c.TryGetProperty("restartCount", out var rc) ? rc.GetInt32() : 0);

                var age = "-";
                if (item.GetProperty("metadata").TryGetProperty("creationTimestamp", out var ct) && ct.TryGetDateTime(out var created))
                {
                    var span = DateTime.UtcNow - created;
                    age = span.TotalHours >= 1 ? $"{(int)span.TotalHours}h" : $"{(int)span.TotalMinutes}m";
                }

                var isOk = (phase == "Running" && readyCount == totalContainers) || phase == "Succeeded";
                list.Add((name, phase, readyStr, restarts, age, isOk));
            }

            return [.. list.OrderBy(x => x.Name, StringComparer.Ordinal)];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Writes the pod status list to the console as a table or JSON.</summary>
    /// <param name="output">Output writer.</param>
    /// <param name="pods">Pod list.</param>
    public static void WritePods(OutputWriter output, IReadOnlyList<(string Name, string Status, string Ready, int Restarts, string Age, bool Ok)> pods)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                ok = pods.Count > 0 && pods.All(p => p.Ok),
                pods = pods.Select(p => new { p.Name, p.Status, p.Ready, p.Restarts, p.Age, ok = p.Ok }),
            });
            return;
        }

        output.Table(
            ["Pod", "Status", "Ready", "Restarts", "Age", "Ok"],
            pods.Select(p => (IReadOnlyList<string>)[p.Name, p.Status, p.Ready, p.Restarts.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Age, p.Ok ? "yes" : "NO"]));
    }

    /// <summary>Establishes port forwarding for all key services in Kubernetes.</summary>
    /// <param name="mappings">List of (Service, LocalPort, RemotePort) tuples.</param>
    /// <param name="output">Output writer.</param>
    /// <param name="cancellationToken">Cancellation token to gracefully close the tunnels.</param>
    /// <returns>Exit code.</returns>
    public async Task<int> PortForward(IReadOnlyList<(string Service, int LocalPort, int RemotePort)> mappings, OutputWriter output, CancellationToken cancellationToken)
    {
        output.Line("Establishing port forwarding to Kubernetes services...");
        var processes = new List<Process>();

        try
        {
            foreach (var (service, localPort, remotePort) in mappings)
            {
                var start = new ProcessStartInfo("kubectl")
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    ArgumentList = { "port-forward", $"svc/{service}", $"{localPort}:{remotePort}", "--address", "0.0.0.0" },
                };

                var proc = Process.Start(start);
                if (proc is not null)
                {
                    processes.Add(proc);
                }
            }

            output.Table(
                ["Service", "Local Port", "Remote Port", "URL"],
                mappings.Select(m => (IReadOnlyList<string>)[m.Service, m.LocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture), m.RemotePort.ToString(System.Globalization.CultureInfo.InvariantCulture), $"http://localhost:{m.LocalPort}"]));

            output.Line();
            output.Line("Port forwarding active. Press Ctrl+C to terminate...");

            var tcs = new TaskCompletionSource();
            using var reg = cancellationToken.Register(() => tcs.TrySetResult());
            await tcs.Task;
            return ExitCodes.Success;
        }
        finally
        {
            output.Line("Stopping port forwards...");
            foreach (var proc in processes)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore process already dead
                }

                proc.Dispose();
            }
        }
    }

    /// <summary>Tails or streams logs of a service in Kubernetes.</summary>
    /// <param name="serviceName">Name of the service or pod.</param>
    /// <param name="follow">Whether to follow/stream output.</param>
    /// <param name="output">Output writer.</param>
    /// <returns>Exit code.</returns>
    public int Logs(string serviceName, bool follow, OutputWriter output)
    {
        var label = serviceName.EndsWith("-api", StringComparison.Ordinal) || serviceName.EndsWith("-bff", StringComparison.Ordinal) || serviceName.EndsWith("-worker", StringComparison.Ordinal)
            ? serviceName
            : $"{serviceName}-api";

        var args = new List<string> { "logs", "-l", $"app.kubernetes.io/name={label}", "--tail=100" };
        if (follow)
        {
            args.Add("-f");
        }

        var start = new ProcessStartInfo("kubectl")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var proc = Process.Start(start);
            proc?.WaitForExit();
            return proc?.ExitCode ?? ExitCodes.Failed;
        }
        catch (Exception ex)
        {
            output.Error($"Failed to fetch logs: {ex.Message}");
            return ExitCodes.Failed;
        }
    }

    /// <summary>Enables or disables hybrid development mode by scaling the service in Kubernetes.</summary>
    /// <param name="serviceName">Service name.</param>
    /// <param name="stop">Whether to restore normal replicas.</param>
    /// <param name="output">Output writer.</param>
    /// <returns>Exit code.</returns>
    public int Dev(string serviceName, bool stop, OutputWriter output)
    {
        var normalized = serviceName.ToLowerInvariant().Replace("-api", "");
        var targetDeploy = $"{normalized}-api";
        var workerDeploy = $"{normalized}-worker";

        if (stop)
        {
            output.Line($"Restoring replicas for {normalized} in Kubernetes...");
            RunKubectl(output, "scale", $"deploy/{targetDeploy}", "--replicas=1");
            if (DeploymentExists(workerDeploy))
            {
                RunKubectl(output, "scale", $"deploy/{workerDeploy}", "--replicas=1");
            }

            output.Line($"Replicas for {normalized} restored to 1.");
            return ExitCodes.Success;
        }

        output.Line($"Scaling down {normalized} in Kubernetes for local IDE development...");
        RunKubectl(output, "scale", $"deploy/{targetDeploy}", "--replicas=0");
        if (DeploymentExists(workerDeploy))
        {
            RunKubectl(output, "scale", $"deploy/{workerDeploy}", "--replicas=0");
        }

        output.Line();
        output.Line($"==> {normalized} scaled to 0 in cluster. Configure your local IDE with:");
        output.Table(
            ["Configuration Key", "Value"],
            [
                ["ConnectionStrings__Write", $"Server=localhost,1433;Database=SuperApp;User Id={normalized}_app;Password=Dev!Passw0rd1;TrustServerCertificate=True"],
                ["ConnectionStrings__Read", $"Server=localhost,1433;Database=SuperApp;User Id={normalized}_app;Password=Dev!Passw0rd1;TrustServerCertificate=True"],
                ["ConnectionStrings__RabbitMq", "amqp://guest:guest@localhost:5672/"],
                ["ConnectionStrings__Redis", "localhost:6379"],
                ["Authentication__Authority", "http://localhost:8081/realms/superapp"],
                ["Authentication__RequireHttpsMetadata", "false"],
            ]);
        output.Line();
        output.Line($"When done debugging, run: dotnet superapp env dev {normalized} --stop");
        return ExitCodes.Success;
    }

    private int RunKubectl(OutputWriter output, params string[] arguments)
    {
        var start = new ProcessStartInfo("kubectl")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = false,
        };
        foreach (var arg in arguments)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("kubectl could not be started.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            output.Error($"kubectl is not available ({exception.Message}); install kubectl and ensure it is in PATH.");
            return ExitCodes.Failed;
        }
    }

    private string ReadKubectl(params string[] arguments)
    {
        var start = new ProcessStartInfo("kubectl")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in arguments)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("kubectl could not be started.");
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return text;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException($"kubectl is not available ({exception.Message}).", exception);
        }
    }

    private int RunHelm(OutputWriter output, params string[] arguments)
    {
        var start = new ProcessStartInfo("helm")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = false,
        };
        foreach (var arg in arguments)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("helm could not be started.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            output.Error($"helm is not available ({exception.Message}); install Helm and ensure it is in PATH.");
            return ExitCodes.Failed;
        }
    }

    private bool DeploymentExists(string name)
    {
        try
        {
            var start = new ProcessStartInfo("kubectl")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ArgumentList = { "get", "deploy", name },
            };
            using var proc = Process.Start(start);
            if (proc is null)
            {
                return false;
            }

            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
