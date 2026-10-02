using System.CommandLine;
using SuperApp.Cli.LocalEnvironment;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp e2e</c>: runs the end-to-end scenario against the local environment started with <c>env up</c>.</summary>
/// <remarks>
/// Checks the contracts between gateway, BFFs, services and the CIAM (<see cref="E2eScenario"/>) and prints one line per check. Exit code
/// <see cref="ExitCodes.FindingsFound"/> when a check fails. Run it after a change of routing, security, error handling or the compose
/// file, and before handing over a change that crosses components (checklist of the developer guide).
/// </remarks>
internal static class E2eCommand
{
    /// <summary>Creates the command.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>e2e</c> command.</returns>
    public static Command Create(CommonOptions common)
    {
        var command = new Command("e2e", "Run the end-to-end scenario against the running local environment (dotnet superapp env up first).");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            using var http = new LocalHttp();
            var checks = await E2eScenario.RunAsync(RepositoryScanner.Scan(root), http, cancellationToken);
            var failed = checks.Count(check => !check.Ok);
            if (output.IsJson)
            {
                output.WriteJson(new { passed = checks.Count - failed, failed, checks });
            }
            else
            {
                foreach (var check in checks)
                {
                    output.Line($"{(check.Ok ? "ok  " : "FAIL")}  {check.Name}{(check.Ok ? string.Empty : $"  [{check.Detail}]")}");
                }

                output.Line();
                output.Line($"{checks.Count - failed}/{checks.Count} checks passed.");
            }

            return failed == 0 ? ExitCodes.Success : ExitCodes.FindingsFound;
        });
        return command;
    }
}
