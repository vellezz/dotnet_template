using System.Text.RegularExpressions;

namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Registers and unregisters the write context of a service in <c>SuperApp.Migrator/Program.cs</c> (ADR-0004).</summary>
/// <remarks>
/// Three places: the <c>using</c> of the context's namespace, the <c>AddDbContext&lt;{Service}WriteDbContext&gt;</c> line next to the other
/// services, and <c>typeof({Service}WriteDbContext)</c> at the end of the <c>contexts</c> array, which fixes the order of migration.
/// </remarks>
internal static partial class MigratorEditor
{
    /// <summary>Path of the Migrator project file.</summary>
    public const string ProjectPath = "src/Migrator/SuperApp.Migrator/SuperApp.Migrator.csproj";

    /// <summary>Path of the Migrator program.</summary>
    public const string ProgramPath = "src/Migrator/SuperApp.Migrator/Program.cs";

    /// <summary>Adds the service's context to the program.</summary>
    /// <param name="content">Content of <c>Program.cs</c>.</param>
    /// <param name="service">Service name, e.g. <c>Billing</c>.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The program no longer has the expected layout.</exception>
    public static string Register(string content, string service)
    {
        var context = $"{service}WriteDbContext";
        if (!content.Contains($"using {service}.Infrastructure.Persistence.Write;", StringComparison.Ordinal))
        {
            content = TextEdits.InsertAfterLast(content, TextEdits.IsUsingDirective,
                [$"using {service}.Infrastructure.Persistence.Write;"], "the using directives of the Migrator");
        }

        if (!content.Contains($"AddDbContext<{context}>", StringComparison.Ordinal))
        {
            content = TextEdits.InsertAfterLast(content, line => line.Contains("MigrationOptions.Configure(options, connectionString,", StringComparison.Ordinal),
                [$"builder.Services.AddDbContext<{context}>(options => MigrationOptions.Configure(options, connectionString, {context}.SchemaName));"],
                "the AddDbContext of the last service in the Migrator");
        }

        if (!content.Contains($"typeof({context})", StringComparison.Ordinal))
        {
            var match = ContextsArray().Match(content);
            if (!match.Success)
            {
                throw new ScaffoldException($"Cannot find the contexts array in {ProgramPath}; append typeof({context}) by hand.");
            }

            content = content.Insert(match.Groups[1].Index + match.Groups[1].Length, $", typeof({context})");
        }

        return content;
    }

    /// <summary>Removes the service's context from the program.</summary>
    /// <param name="content">Content of <c>Program.cs</c>.</param>
    /// <param name="service">Service name.</param>
    /// <returns>The new content.</returns>
    public static string Unregister(string content, string service)
    {
        var context = $"{service}WriteDbContext";
        content = TextEdits.RemoveLines(content, line => line == $"using {service}.Infrastructure.Persistence.Write;"
            || line.Contains($"AddDbContext<{context}>", StringComparison.Ordinal));
        return content.Replace($", typeof({context})", string.Empty, StringComparison.Ordinal)
            .Replace($"typeof({context}), ", string.Empty, StringComparison.Ordinal);
    }

    [GeneratedRegex("Type\\[\\] contexts = \\[(.*?)\\];")]
    private static partial Regex ContextsArray();
}
