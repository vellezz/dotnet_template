using System.CommandLine;
using System.Text;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding;
using SuperApp.Cli.Scaffolding.Plans;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp migration add|list|script</c>: EF Core migrations of every write context (ADR-0004).</summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>add &lt;Service|Gateway&gt; &lt;Name&gt;</c>: a new migration with the right project, context and output directory,
///   the file named after its type and a summary to fill in (an applied migration is never edited: a change is a new migration);</description></item>
///   <item><description><c>list [&lt;Service|Gateway&gt;]</c>: the migrations of each context, read from the files (no build, no database);</description></item>
///   <item><description><c>script [--output &lt;file&gt;]</c>: one idempotent SQL script of all contexts in the order of the Migrator job, the
///   artifact the DBA runs on production before the deployment (on production nothing migrates by itself).</description></item>
/// </list>
/// </remarks>
internal static class MigrationCommand
{
    private const string DefaultScript = "artifacts/migrations/migrations.sql";

    /// <summary>Creates the command with its subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>migration</c> command.</returns>
    public static Command Create(CommonOptions common) =>
        new("migration", "EF Core migrations of the gateway and the services: add one, list them, script them for the DBA.")
        {
            Add(common),
            List(common),
            Script(common),
        };

    private static Command Add(CommonOptions common)
    {
        var target = new Argument<string>("context") { Description = "Gateway or a service name, e.g. Knowledge." };
        var name = new Argument<string>("name") { Description = "Migration name in PascalCase, e.g. AddInvoiceDueDate." };
        var dryRun = new Option<bool>("--dry-run") { Description = "List the steps without changing anything." };
        var command = new Command("add", "Add a migration to a write context (builds the project, runs dotnet ef, names the file after its type).")
        {
            target,
            name,
            dryRun,
        };
        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => MigrationPlans.Add(model, parseResult.GetValue(target)!, parseResult.GetValue(name)!), apply: !parseResult.GetValue(dryRun)));
        return command;
    }

    private static Command List(CommonOptions common)
    {
        var target = new Argument<string?>("context") { Description = "Gateway or a service name; all contexts when omitted.", Arity = ArgumentArity.ZeroOrOne };
        var command = new Command("list", "List the migrations of each write context, in the order the Migrator applies the contexts.") { target };
        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var model = RepositoryScanner.Scan(root);
            var wanted = parseResult.GetValue(target);
            var targets = MigrationTargets.All(model).Where(t => wanted is null || string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            if (targets.Count == 0)
            {
                output.Error($"There is no context {wanted}; known: {string.Join(", ", MigrationTargets.All(model).Select(t => t.Name))}.");
                return ExitCodes.NotFound;
            }

            var rows = targets.SelectMany(t => MigrationTargets.Migrations(model.Files, t).Select(id => (t.Name, t.Context, Id: id))).ToList();
            if (output.IsJson)
            {
                output.WriteJson(rows.Select(row => new { context = row.Name, type = row.Context, id = row.Id }));
            }
            else
            {
                output.Table(["Context", "Migration", "Created"],
                    rows.Select(row => (IReadOnlyList<string>)[row.Name, row.Id[(row.Id.IndexOf('_', StringComparison.Ordinal) + 1)..], Timestamp(row.Id)]));
            }

            return ExitCodes.Success;
        });
        return command;
    }

    private static Command Script(CommonOptions common)
    {
        var path = new Option<string>("--output") { Description = $"Script file relative to the repository root (default {DefaultScript})." };
        var command = new Command("script", "Write one idempotent SQL script of all contexts (Migrator order) for the DBA.") { path };
        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var model = RepositoryScanner.Scan(root);
            var runner = new ProcessRunner(root);
            var target = parseResult.GetValue(path) ?? DefaultScript;
            try
            {
                // The Migrator references the gateway and every service infrastructure: one build compiles all contexts.
                runner.Dotnet("build", "src/Migrator/SuperApp.Migrator", "--nologo", "-v", "q");
                var script = new StringBuilder();
                script.Append("-- Idempotent migrations of all write contexts, in the order of the Migrator job (ADR-0004).\n");
                script.Append("-- Generated by: dotnet superapp migration script. Review before running; every statement checks __EFMigrationsHistory.\n");
                var temporary = Path.Combine(Path.GetTempPath(), $"superapp-migration-{Guid.NewGuid():N}.sql");
                var contexts = MigrationTargets.All(model);
                foreach (var context in contexts)
                {
                    runner.Dotnet("ef", "migrations", "script", "--idempotent", "-p", context.Project, "-s", context.Project,
                        "--context", context.Context, "-o", temporary, "--no-build");
                    script.Append($"\n-- ===== {context.Name} ({context.Context}) =====\n");
                    script.Append(File.ReadAllText(temporary).Replace("\r\n", "\n", StringComparison.Ordinal));
                    File.Delete(temporary);
                }

                var full = model.Files.FullPath(target);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, script.ToString(), new UTF8Encoding(false));
                Write(output, target, contexts);
                return ExitCodes.Success;
            }
            catch (ScaffoldException exception)
            {
                output.Error(exception.Message);
                return ExitCodes.Failed;
            }
        });
        return command;
    }

    private static void Write(OutputWriter output, string target, IReadOnlyList<MigrationTarget> contexts)
    {
        if (output.IsJson)
        {
            output.WriteJson(new { script = target, contexts = contexts.Select(context => context.Name) });
            return;
        }

        output.Line($"{target}: {string.Join(", ", contexts.Select(context => context.Name))}");
    }

    private static string Timestamp(string id) =>
        id.Length >= 14 && id[..14].All(char.IsDigit) ? $"{id[..4]}-{id[4..6]}-{id[6..8]} {id[8..10]}:{id[10..12]}" : "-";
}
