using System.CommandLine;
using SuperApp.Cli.Scaffolding;
using SuperApp.Cli.Scaffolding.Editors;
using SuperApp.Cli.Scaffolding.Plans;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp helm generate|values</c>: manages the Helm umbrella chart and environment values for ArgoCD (ADR-0016).</summary>
internal static class HelmCommand
{
    /// <summary>Creates the <c>helm</c> command with its subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>helm</c> command.</returns>
    public static Command Create(CommonOptions common) =>
        new("helm", "Manage the Helm umbrella chart and environment values for ArgoCD (ADR-0016).")
        {
            Generate(common),
            Values(common),
        };

    private static Command Generate(CommonOptions common)
    {
        var output = new Option<string>("--output")
        {
            Description = $"Target directory for the umbrella chart (default: {UmbrellaChartEditor.DefaultDirectory}).",
            DefaultValueFactory = _ => UmbrellaChartEditor.DefaultDirectory,
        };
        var dryRun = new Option<bool>("--dry-run") { Description = "List the files that would be created without changing anything." };

        var command = new Command("generate", "Generate or update the Helm umbrella chart (Chart.yaml, values.yaml and values-<env>.yaml) from the registered services and BFFs.")
        {
            output,
            dryRun,
        };

        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => HelmPlans.Generate(model, parseResult.GetValue(output)!),
            apply: !parseResult.GetValue(dryRun)));

        return command;
    }

    private static Command Values(CommonOptions common)
    {
        var env = new Option<string>("--env")
        {
            Description = "Target environment for the values file (dev, test, prod).",
            DefaultValueFactory = _ => "dev",
        };
        env.AcceptOnlyFromAmong(["dev", "test", "prod"]);

        var output = new Option<string?>("--output")
        {
            Description = "Path to write the values file to (e.g. into your external ArgoCD/GitOps repository); defaults to artifacts/helm/superapp/values-<env>.yaml.",
        };
        var dryRun = new Option<bool>("--dry-run") { Description = "List the steps without writing files." };

        var command = new Command("values", "Generate values for a specific environment (dev, test, prod) ready for the ArgoCD repository.")
        {
            env,
            output,
            dryRun,
        };

        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => HelmPlans.Values(model, parseResult.GetValue(env)!, parseResult.GetValue(output)),
            apply: !parseResult.GetValue(dryRun)));

        return command;
    }
}
