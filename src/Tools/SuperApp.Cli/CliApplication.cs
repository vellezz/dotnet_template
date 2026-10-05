using System.CommandLine;
using SuperApp.Cli.Commands;

namespace SuperApp.Cli;

/// <summary>Defines the command line of <c>dotnet superapp</c>: the root command, shared options and all subcommands (ADR-0046).</summary>
/// <remarks>
/// <para>
/// Read-only commands: <c>doctor</c> (consistency checks, <c>--fix</c> for mechanical fixes), <c>list</c> and <c>info</c>. Scaffolding commands:
/// <c>add</c>/<c>remove</c> for services, BFFs and service clients of BFFs. Everyday work: <c>migration add|list|script</c> and
/// <c>contracts [--check]</c>, and <c>add|remove usecase|aggregate|event|consumer|scope|flag|product-event</c>. Local environment:
/// <c>env up|down|status|token</c> and the end-to-end scenario <c>e2e</c>.
/// </para>
/// <para>
/// Every command works without prompts, accepts <c>--json</c> and returns the codes of <see cref="ExitCodes"/>, so it can be run by CI
/// and by assistants (Copilot) as well as by people.
/// </para>
/// </remarks>
internal static class CliApplication
{
    /// <summary>Creates the root command with all subcommands.</summary>
    /// <returns>The root command, ready to parse arguments.</returns>
    public static RootCommand Create()
    {
        var common = new CommonOptions();
        return new RootCommand("Developer tool of the SuperApp repository: consistency checks, an overview, and adding or removing services, BFFs and service clients.")
        {
            common.Root,
            common.Json,
            DoctorCommand.Create(common),
            ListCommand.Create(common),
            InfoCommand.Create(common),
            AddCommand.Create(common),
            RemoveCommand.Create(common),
            MigrationCommand.Create(common),
            ContractsCommand.Create(common),
            HelmCommand.Create(common),
            EnvCommand.Create(common),
            E2eCommand.Create(common),
        };
    }
}
