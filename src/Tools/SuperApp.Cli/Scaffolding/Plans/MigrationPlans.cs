using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Scaffolding.Plans;

/// <summary>Plan of <c>migration add</c> and the shared step that creates an EF Core migration the way the repository keeps them.</summary>
/// <remarks>
/// <para>
/// <c>dotnet ef migrations add</c> needs the right project, startup project, context and output directory for every context; getting one
/// of them wrong is the most common mistake (recipe 04). The step builds the project first (so a missing restore or a compile error is
/// reported clearly), runs <c>dotnet ef --no-build</c>, renames <c>{timestamp}_{Name}.cs</c> to <c>{Name}.cs</c> (the file is named
/// after its type, ADR-0032; the designer file keeps the timestamp) and replaces the class's <c>&lt;inheritdoc /&gt;</c> with a summary.
/// </para>
/// <para>
/// A migration is reviewed by a person: whether it is expand or contract, whether it loses data (chapter 07, ADR-0004). The summary
/// written by <c>migration add</c> is a TODO for that review; the summaries of the gateway route migrations of <c>add bff</c> are final.
/// </para>
/// </remarks>
internal static class MigrationPlans
{
    /// <summary>Builds the plan of <c>migration add</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="targetName"><c>Gateway</c> or a service name.</param>
    /// <param name="name">Migration name in PascalCase, e.g. <c>AddInvoiceDueDate</c>.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The target does not exist, the name is invalid or a migration of that name exists.</exception>
    public static ScaffoldPlan Add(RepositoryModel model, string targetName, string name)
    {
        var target = MigrationTargets.Find(model, targetName)
            ?? throw new ScaffoldException($"There is no context {targetName}; known: {string.Join(", ", MigrationTargets.All(model).Select(t => t.Name))}.");
        Naming.EnsurePascalCase(name, "migration");
        if (MigrationTargets.Migrations(model.Files, target).Any(id => id.EndsWith($"_{name}", StringComparison.Ordinal)))
        {
            throw new ScaffoldException($"{target.Name} already has a migration {name}; choose another name (an applied migration is never edited).");
        }

        var summary = $"TODO: what this migration changes, whether it is an expand or a contract step and whether it can lose data (chapter 07, ADR-0004).";
        return new ScaffoldPlan($"migration add {target.Name} {name}",
        [
            Step(target, name, summary),
        ],
        [
            $"Review {target.MigrationsPath}/{name}.cs: expand/contract (old and new application versions must work on the schema during a rolling update), destructive operations only in a contract step, data migrations in SQL inside the migration.",
            $"Replace the TODO in the summary of {name} with what the migration does and why.",
            "Run the integration tests (they apply all migrations on Testcontainers) and dotnet superapp doctor; for the DBA: dotnet superapp migration script.",
        ]);
    }

    /// <summary>The step that creates a migration of a target with a given class summary.</summary>
    /// <param name="target">The context that owns the migration.</param>
    /// <param name="name">Migration name in PascalCase.</param>
    /// <param name="summary">Text of the <c>&lt;summary&gt;</c> of the migration class.</param>
    /// <returns>The step.</returns>
    public static ScaffoldStep Step(MigrationTarget target, string name, string summary) =>
        new($"{target.MigrationsPath}/{name}.cs", $"migration {name} of {target.Context} (dotnet ef, builds {target.Project})", context =>
        {
            var file = $"{target.MigrationsPath}/{name}.cs";
            if (context.Files.Exists(file))
            {
                return [new StepResult(StepStatus.Unchanged, file, "migration already exists")];
            }

            // dotnet ef needs a restored and compiling project; building first also gives a clear error when the model does not compile.
            var built = context.Runner.Dotnet("build", target.Project, "--nologo", "-v", "q");
            var ran = context.Runner.Dotnet("ef", "migrations", "add", name, "-p", target.Project, "-s", target.Project,
                "--context", target.Context, "-o", target.MigrationsDirectory, "--no-build");
            var generated = context.Files.Files(target.MigrationsPath, $"*_{name}.cs").Single(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));
            File.Move(context.Files.FullPath(generated), context.Files.FullPath(file));
            context.Update(file, "summary", content => Describe(content, name, summary));
            return [built, ran, new StepResult(StepStatus.Created, file, $"renamed from {Path.GetFileName(generated)}")];
        });

    // EF writes "/// <inheritdoc />" above the class; the repository documents every migration (ADR-0033).
    private static string Describe(string content, string name, string summary)
    {
        var lines = TextEdits.Lines(content);
        var index = lines.FindIndex(line => line.TrimStart().StartsWith($"public partial class {name} ", StringComparison.Ordinal));
        if (index > 0 && lines[index - 1].Trim() == "/// <inheritdoc />")
        {
            var indent = lines[index][..(lines[index].Length - lines[index].TrimStart().Length)];
            lines[index - 1] = $"{indent}/// <summary>{summary}</summary>";
        }

        return TextEdits.Join(lines, content);
    }
}
