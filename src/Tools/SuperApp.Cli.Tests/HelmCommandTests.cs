using System.CommandLine;
using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding.Editors;

namespace SuperApp.Cli.Tests;

/// <summary>Unit and command tests for <c>dotnet superapp helm generate</c> and <c>dotnet superapp helm values</c>.</summary>
public sealed class HelmCommandTests
{
    private static readonly string Root = RepositoryRoot.Find(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Tests must run inside the repository.");

    private static readonly RepositoryModel Model = RepositoryScanner.Scan(Root);

    [Fact]
    public void Umbrella_chart_yaml_contains_all_services_and_bffs()
    {
        var yaml = UmbrellaChartEditor.ChartYaml(Model);

        Assert.Contains("name: superapp", yaml, StringComparison.Ordinal);
        Assert.Contains("alias: migrator", yaml, StringComparison.Ordinal);
        Assert.Contains("alias: analytics-forwarder", yaml, StringComparison.Ordinal);
        foreach (var service in Model.Services)
        {
            Assert.Contains($"alias: {service.Key}", yaml, StringComparison.Ordinal);
        }

        foreach (var bff in Model.Bffs)
        {
            Assert.Contains($"alias: {bff.Key}-bff", yaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Umbrella_values_yaml_contains_identities_and_downstreams()
    {
        var yaml = UmbrellaChartEditor.ValuesYaml(Model);

        foreach (var service in Model.Services)
        {
            Assert.Contains($"{service.Key}:", yaml, StringComparison.Ordinal);
            Assert.Contains($"secretName: {service.Key}-secrets", yaml, StringComparison.Ordinal);
        }

        foreach (var bff in Model.Bffs)
        {
            Assert.Contains($"{bff.Key}-bff:", yaml, StringComparison.Ordinal);
            Assert.Contains($"secretName: {bff.Key}-bff-secrets", yaml, StringComparison.Ordinal);
            if (bff.Clients.Count > 0)
            {
                Assert.Contains("downstream:", yaml, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Environment_values_differ_between_dev_and_prod()
    {
        var dev = UmbrellaChartEditor.EnvValuesYaml(Model, "dev");
        var prod = UmbrellaChartEditor.EnvValuesYaml(Model, "prod");

        Assert.Contains("enabled: true", dev, StringComparison.Ordinal);
        Assert.Contains("enabled: false", prod, StringComparison.Ordinal);
        Assert.Contains("imageTag: \"dev\"", dev, StringComparison.Ordinal);
        Assert.Contains("imageTag: \"1.0.0\"", prod, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Helm_generate_dry_run_plans_all_files()
    {
        var (exitCode, output, _) = await Run("helm", "generate", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Chart.yaml", output, StringComparison.Ordinal);
        Assert.Contains("values.yaml", output, StringComparison.Ordinal);
        Assert.Contains("values-dev.yaml", output, StringComparison.Ordinal);
        Assert.Contains("values-prod.yaml", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Helm_values_dry_run_plans_environment_file()
    {
        var (exitCode, output, _) = await Run("helm", "values", "--env", "prod", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("values-prod.yaml", output, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CliApplication.Create()
            .Parse([.. args, "--root", Root])
            .InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }
}
