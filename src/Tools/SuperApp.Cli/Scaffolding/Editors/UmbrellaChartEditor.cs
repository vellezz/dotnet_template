using System.Text;
using System.Text.RegularExpressions;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Generates and edits the Helm umbrella chart (Chart.yaml, values.yaml) and environment values for ArgoCD (ADR-0016).</summary>
internal static partial class UmbrellaChartEditor
{
    public const string DefaultDirectory = "artifacts/helm/superapp";

    public static string ChartPath(string directory = DefaultDirectory) =>
        $"{directory.TrimEnd('/', '\\')}/Chart.yaml".Replace('\\', '/');

    public static string ValuesPath(string directory = DefaultDirectory) =>
        $"{directory.TrimEnd('/', '\\')}/values.yaml".Replace('\\', '/');

    public static string EnvValuesPath(string env, string directory = DefaultDirectory) =>
        $"{directory.TrimEnd('/', '\\')}/values-{env.ToLowerInvariant()}.yaml".Replace('\\', '/');

    /// <summary>Generates the Chart.yaml for the umbrella chart, declaring dependencies to all subcharts.</summary>
    public static string ChartYaml(RepositoryModel model, string directory = DefaultDirectory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: v2");
        sb.AppendLine("name: superapp");
        sb.AppendLine("description: Umbrella chart platformy SuperApp (ADR-0016)");
        sb.AppendLine("type: application");
        sb.AppendLine("version: 0.1.0");
        sb.AppendLine("appVersion: \"1.0.0\"");
        sb.AppendLine();
        sb.AppendLine("dependencies:");
        sb.AppendLine("  - name: superapp-migrator");
        sb.AppendLine("    version: 0.1.0");
        sb.AppendLine($"    repository: {ResolveSubchartRepo(directory, "superapp-migrator")}");
        sb.AppendLine("    alias: migrator");
        sb.AppendLine("  - name: superapp-analytics-forwarder");
        sb.AppendLine("    version: 0.1.0");
        sb.AppendLine($"    repository: {ResolveSubchartRepo(directory, "superapp-analytics-forwarder")}");
        sb.AppendLine("    alias: analytics-forwarder");

        foreach (var service in model.Services.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            sb.AppendLine("  - name: superapp-service");
            sb.AppendLine("    version: 0.1.0");
            sb.AppendLine($"    repository: {ResolveSubchartRepo(directory, "superapp-service")}");
            sb.AppendLine($"    alias: {service.Key}");
        }

        foreach (var bff in model.Bffs.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            sb.AppendLine("  - name: superapp-bff");
            sb.AppendLine("    version: 0.1.0");
            sb.AppendLine($"    repository: {ResolveSubchartRepo(directory, "superapp-bff")}");
            sb.AppendLine($"    alias: {bff.Key}-bff");
        }

        return sb.ToString();
    }

    private static string ResolveSubchartRepo(string targetDirectory, string chartName)
    {
        var relative = Path.GetRelativePath(targetDirectory, $"deploy/helm/{chartName}").Replace('\\', '/');
        return $"\"file://{relative}\"";
    }

    /// <summary>Generates the base values.yaml for the umbrella chart.</summary>
    public static string ValuesYaml(RepositoryModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Wartości bazowe Umbrella Chartu SuperApp (ADR-0016).");
        sb.AppendLine("# Definiuje tożsamość i domyślną konfigurację zarejestrowanych serwisów i BFF-ów.");
        sb.AppendLine("# Nadpisywane wartościami środowiskowymi z repozytorium ArgoCD (values-<env>.yaml).");
        sb.AppendLine();
        sb.AppendLine("global:");
        sb.AppendLine("  imageRegistry: registry.example.local/app");
        sb.AppendLine("  imageTag: \"0.1.0\"");
        sb.AppendLine("  authenticationAuthority: \"\"");
        sb.AppendLine("  otelEndpoint: http://otel-collector.observability.svc.cluster.local:4317");
        sb.AppendLine();
        sb.AppendLine("migrator:");
        sb.AppendLine("  experience: superapp");
        sb.AppendLine("  enabled: true");
        sb.AppendLine("  secretName: superapp-migrator-secrets");
        sb.AppendLine();
        sb.AppendLine("analytics-forwarder:");
        sb.AppendLine("  experience: superapp");
        sb.AppendLine("  secretName: analytics-forwarder-secrets");

        foreach (var service in model.Services.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            sb.AppendLine();
            sb.AppendLine($"{service.Key}:");
            sb.AppendLine($"  experience: {service.Experience ?? "example"}");
            sb.AppendLine($"  service: {service.Key}");
            sb.AppendLine($"  secretName: {service.Key}-secrets");

            var queues = ReadKedaQueues(model, service.Key);
            if (queues.Count > 0)
            {
                sb.AppendLine("  worker:");
                sb.AppendLine("    keda:");
                sb.AppendLine("      queues:");
                foreach (var queue in queues)
                {
                    sb.AppendLine($"        - {queue}");
                }
            }
        }

        foreach (var bff in model.Bffs.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            sb.AppendLine();
            sb.AppendLine($"{bff.Key}-bff:");
            sb.AppendLine($"  experience: {bff.Key}");
            sb.AppendLine($"  secretName: {bff.Key}-bff-secrets");
            if (bff.Clients.Count == 0)
            {
                sb.AppendLine("  downstream: {}");
            }
            else
            {
                sb.AppendLine("  downstream:");
                foreach (var client in bff.Clients.OrderBy(c => c, StringComparer.Ordinal))
                {
                    var clientKey = client.ToLowerInvariant();
                    sb.AppendLine($"    {client}: http://{clientKey}-api.{clientKey}.svc.cluster.local:8080");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>Generates environment values (values-{env}.yaml) ready for the ArgoCD repository.</summary>
    public static string EnvValuesYaml(RepositoryModel model, string env)
    {
        env = env.ToLowerInvariant();
        var sb = new StringBuilder();
        sb.AppendLine($"# Wartości środowiskowe dla ArgoCD (środowisko: {env})");
        sb.AppendLine("# Przekazywane przez ArgoCD jako nakładka na Umbrella Chart.");
        sb.AppendLine();
        sb.AppendLine("global:");

        switch (env)
        {
            case "prod":
                sb.AppendLine("  imageTag: \"1.0.0\"");
                sb.AppendLine("  imageRegistry: \"registry.corp/app\"");
                sb.AppendLine("  authenticationAuthority: \"https://ciam.corp/realms/superapp\"");
                sb.AppendLine("  otelEndpoint: \"http://otel-collector.prod.svc.cluster.local:4317\"");
                sb.AppendLine();
                sb.AppendLine("migrator:");
                sb.AppendLine("  enabled: false # ADR-0004: na prod migracje wykonuje DBA skryptem SQL");
                break;

            case "test":
                sb.AppendLine("  imageTag: \"test\"");
                sb.AppendLine("  imageRegistry: \"registry-test.corp/app\"");
                sb.AppendLine("  authenticationAuthority: \"https://ciam-test.corp/realms/superapp\"");
                sb.AppendLine("  otelEndpoint: \"http://otel-collector.test.svc.cluster.local:4317\"");
                sb.AppendLine();
                sb.AppendLine("migrator:");
                sb.AppendLine("  enabled: true");
                break;

            case "dev":
            default:
                sb.AppendLine("  imageTag: \"dev\"");
                sb.AppendLine("  imageRegistry: \"registry-dev.local/app\"");
                sb.AppendLine("  authenticationAuthority: \"http://keycloak:8080/realms/superapp\"");
                sb.AppendLine("  otelEndpoint: \"http://otel-collector.observability.svc.cluster.local:4317\"");
                sb.AppendLine();
                sb.AppendLine("migrator:");
                sb.AppendLine("  enabled: true # Hook PreSync uruchamia migracje przed wdrożeniem serwisów");
                break;
        }

        // Service scaling / replica overrides per environment
        foreach (var service in model.Services.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            sb.AppendLine();
            sb.AppendLine($"{service.Key}:");
            if (env == "prod")
            {
                sb.AppendLine("  api:");
                sb.AppendLine("    replicas: 2");
                sb.AppendLine("    autoscaling:");
                sb.AppendLine("      enabled: true");
                sb.AppendLine("      minReplicas: 2");
                sb.AppendLine("      maxReplicas: 10");
                sb.AppendLine("  worker:");
                sb.AppendLine("    replicas: 2");
            }
            else
            {
                sb.AppendLine("  api:");
                sb.AppendLine("    replicas: 1");
                sb.AppendLine("    autoscaling:");
                sb.AppendLine("      enabled: false");
                sb.AppendLine("  worker:");
                sb.AppendLine("    replicas: 1");
            }
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> ReadKedaQueues(RepositoryModel model, string serviceKey)
    {
        var valuesFile = $"deploy/helm/superapp-service/values-{serviceKey}.yaml";
        var content = model.Files.ReadOrEmpty(valuesFile);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var lines = TextEdits.Lines(content);
        var queuesIndex = lines.FindIndex(l => l.Trim().StartsWith("queues:", StringComparison.Ordinal));
        if (queuesIndex < 0)
        {
            return [];
        }

        var queues = new List<string>();
        for (var i = queuesIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!line.StartsWith(" ", StringComparison.Ordinal) && !line.StartsWith("\t", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                queues.Add(trimmed[2..].Trim());
            }
        }

        return queues;
    }
}
