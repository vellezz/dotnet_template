using System.CommandLine;
using SuperApp.Cli.LocalEnvironment;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp db query</c>: local database diagnostics (ADR-0021, ADR-0034).</summary>
/// <remarks>
/// Executes diagnostic queries against the local SuperApp database running in Kubernetes or Docker Compose.
/// </remarks>
internal static class DbCommand
{
    /// <summary>Creates the command with its subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>db</c> command.</returns>
    public static Command Create(CommonOptions common) =>
        new("db", "Run diagnostic queries against the local database.")
        {
            Query(common),
        };

    private static Command Query(CommonOptions common)
    {
        var sql = new Argument<string>("sql") { Description = "SQL query string to execute against the SuperApp database." };
        var k8s = new Option<bool>("--k8s") { Description = "Target Kubernetes cluster (auto-detected if cluster is running)." };

        var command = new Command("query", "Execute a diagnostic SQL query against the local SuperApp database.")
        {
            sql,
            k8s,
        };

        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var queryText = parseResult.GetValue(sql);
            if (string.IsNullOrWhiteSpace(queryText))
            {
                output.Error("SQL query cannot be empty.");
                return ExitCodes.InvalidArguments;
            }

            var dbEnv = new DatabaseEnvironment(root);
            var result = dbEnv.QuerySql(queryText, parseResult.GetValue(k8s));
            output.Line(result);
            return ExitCodes.Success;
        });

        return command;
    }
}
