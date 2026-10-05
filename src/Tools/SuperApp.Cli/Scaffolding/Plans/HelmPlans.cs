using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding.Editors;

namespace SuperApp.Cli.Scaffolding.Plans;

/// <summary>Scaffolding plans for generating the Helm umbrella chart and environment values for ArgoCD (ADR-0016).</summary>
internal static class HelmPlans
{
    /// <summary>Builds the plan that generates or updates the umbrella chart (Chart.yaml, values.yaml and default environment values).</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="directory">Target directory of the umbrella chart.</param>
    /// <returns>The scaffolding plan.</returns>
    public static ScaffoldPlan Generate(RepositoryModel model, string directory = UmbrellaChartEditor.DefaultDirectory)
    {
        var chartPath = UmbrellaChartEditor.ChartPath(directory);
        var valuesPath = UmbrellaChartEditor.ValuesPath(directory);
        var devPath = UmbrellaChartEditor.EnvValuesPath("dev", directory);
        var testPath = UmbrellaChartEditor.EnvValuesPath("test", directory);
        var prodPath = UmbrellaChartEditor.EnvValuesPath("prod", directory);

        var steps = new List<ScaffoldStep>
        {
            new(chartPath, "Umbrella Chart.yaml with dependencies to services and BFFs", context =>
                [context.Write(chartPath, UmbrellaChartEditor.ChartYaml(model, directory), "Umbrella Chart.yaml")]),

            new(valuesPath, "Umbrella values.yaml with component identities and downstreams", context =>
                [context.Write(valuesPath, UmbrellaChartEditor.ValuesYaml(model), "Umbrella values.yaml")]),

            new(devPath, "DEV environment values for ArgoCD", context =>
                [context.Write(devPath, UmbrellaChartEditor.EnvValuesYaml(model, "dev"), "values-dev.yaml for ArgoCD")]),

            new(testPath, "TEST environment values for ArgoCD", context =>
                [context.Write(testPath, UmbrellaChartEditor.EnvValuesYaml(model, "test"), "values-test.yaml for ArgoCD")]),

            new(prodPath, "PROD environment values for ArgoCD", context =>
                [context.Write(prodPath, UmbrellaChartEditor.EnvValuesYaml(model, "prod"), "values-prod.yaml for ArgoCD")]),
        };

        return new ScaffoldPlan($"helm generate --output {directory}", steps,
        [
            $"Test rendering with: helm template superapp {directory} -f {devPath}",
            "To deploy via ArgoCD, point the Argo Application to this umbrella chart with the matching values-<env>.yaml file.",
        ]);
    }

    /// <summary>Builds the plan that generates values for a specific environment (ready to be copied or written to the ArgoCD repository).</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="env">Environment name (dev, test, prod).</param>
    /// <param name="outputPath">Optional output path (defaults to deploy/helm/superapp/values-{env}.yaml).</param>
    /// <returns>The scaffolding plan.</returns>
    public static ScaffoldPlan Values(RepositoryModel model, string env, string? outputPath = null)
    {
        var targetFile = string.IsNullOrWhiteSpace(outputPath)
            ? UmbrellaChartEditor.EnvValuesPath(env)
            : outputPath.Replace('\\', '/');

        var steps = new List<ScaffoldStep>
        {
            new(targetFile, $"{env.ToUpperInvariant()} values for ArgoCD", context =>
                [context.Write(targetFile, UmbrellaChartEditor.EnvValuesYaml(model, env), $"values-{env}.yaml for ArgoCD")]),
        };

        return new ScaffoldPlan($"helm values --env {env}", steps,
        [
            $"Values file written to {targetFile}.",
            "Commit or copy this file to your ArgoCD / GitOps repository.",
        ]);
    }
}
