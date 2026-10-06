using System.CommandLine;
using SuperApp.Cli.LocalEnvironment;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp db seed|clean|query</c>: local database test data management and diagnostics (ADR-0021, ADR-0034).</summary>
/// <remarks>
/// Seeds realistic domain data into write contexts for local development and rapid manual/E2E testing, wipes data cleanly,
/// or executes diagnostic queries against the local SuperApp database running in Kubernetes or Docker Compose.
/// </remarks>
internal static class DbCommand
{
    /// <summary>Creates the command with its subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>db</c> command.</returns>
    public static Command Create(CommonOptions common) =>
        new("db", "Manage local database data: seed test data, clean service tables, or run queries.")
        {
            Seed(common),
            Clean(common),
            Query(common),
        };

    private static Command Seed(CommonOptions common)
    {
        var service = new Argument<string?>("service")
        {
            Description = "Target service (knowledge, sleepdiary) or omitted for all services.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var clean = new Option<bool>("--clean") { Description = "Wipe existing service data before inserting seed records." };
        var k8s = new Option<bool>("--k8s") { Description = "Target Kubernetes cluster (auto-detected if cluster is running)." };

        var command = new Command("seed", "Seed realistic sample data into local databases for development and API exploration.")
        {
            service,
            clean,
            k8s,
        };

        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var dbEnv = new DatabaseEnvironment(root);
            return dbEnv.Seed(
                parseResult.GetValue(service),
                parseResult.GetValue(clean),
                parseResult.GetValue(k8s),
                output);
        });

        return command;
    }

    private static Command Clean(CommonOptions common)
    {
        var service = new Argument<string?>("service")
        {
            Description = "Target service (knowledge, sleepdiary) or omitted for all services.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var k8s = new Option<bool>("--k8s") { Description = "Target Kubernetes cluster (auto-detected if cluster is running)." };

        var command = new Command("clean", "Wipe data from service tables while preserving schema and migrations.")
        {
            service,
            k8s,
        };

        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var dbEnv = new DatabaseEnvironment(root);
            output.Line("Cleaning database tables...");
            var exit = dbEnv.Clean(parseResult.GetValue(service), parseResult.GetValue(k8s), output);
            if (exit == ExitCodes.Success)
            {
                output.Line("Database clean completed.");
            }

            return exit;
        });

        return command;
    }

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
