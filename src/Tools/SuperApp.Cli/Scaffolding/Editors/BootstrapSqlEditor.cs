using System.Text.RegularExpressions;

namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Adds and removes a schema in the <c>@Services</c> list of <c>deploy/sql/01-bootstrap.sql</c> (ADR-0021).</summary>
/// <remarks>
/// The list is a run of <c>(N'schema'),</c> lines ending with <c>;</c>. The editor rewrites the whole run, so the commas and the final
/// semicolon stay right after adding or removing a row. Removing a row only stops new environments from creating the schema, role and
/// login; an existing database is never changed by the tool (the DBA drops them).
/// </remarks>
internal static partial class BootstrapSqlEditor
{
    /// <summary>Path of the bootstrap script.</summary>
    public const string Path = "deploy/sql/01-bootstrap.sql";

    /// <summary>Adds a schema to the list.</summary>
    /// <param name="content">Content of the script.</param>
    /// <param name="schema">Schema name, the lower-case service name.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The list cannot be found.</exception>
    public static string Add(string content, string schema) => Rewrite(content, schemas => schemas.Contains(schema) ? schemas : [.. schemas, schema]);

    /// <summary>Removes a schema from the list.</summary>
    /// <param name="content">Content of the script.</param>
    /// <param name="schema">Schema name.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The list cannot be found.</exception>
    public static string Remove(string content, string schema) => Rewrite(content, schemas => [.. schemas.Where(existing => existing != schema)]);

    private static string Rewrite(string content, Func<List<string>, List<string>> change)
    {
        var lines = TextEdits.Lines(content);
        var rows = lines.Select((line, index) => (match: Row().Match(line), index)).Where(item => item.match.Success).ToList();
        if (rows.Count == 0)
        {
            throw new ScaffoldException($"Cannot find the @Services list in {Path}; change it by hand.");
        }

        var indent = rows[0].match.Groups[1].Value;
        var schemas = rows.Select(row => row.match.Groups[2].Value).ToList();
        var updated = change(schemas);
        if (updated.SequenceEqual(schemas))
        {
            return content;
        }

        if (updated.Count == 0)
        {
            throw new ScaffoldException($"Removing the last schema would leave {Path} without services; change it by hand.");
        }

        var first = rows[0].index;
        lines.RemoveRange(first, rows.Count);
        lines.InsertRange(first, updated.Select((schema, index) => $"{indent}(N'{schema}'){(index == updated.Count - 1 ? ";" : ",")}"));
        return TextEdits.Join(lines, content);
    }

    [GeneratedRegex("^(\\s*)\\(N'([a-z0-9_]+)'\\)[,;]\\s*$")]
    private static partial Regex Row();
}
