namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Adds and removes <c>&lt;ProjectReference&gt;</c> lines of a <c>.csproj</c>, keeping the references sorted.</summary>
/// <remarks>Used for the Migrator (Infrastructure projects of the services) and the architecture tests (Api, Worker and BFF projects).</remarks>
internal static class ProjectFileEditor
{
    /// <summary>Adds a reference after the existing ones, in ordinal order; no change when it is already there.</summary>
    /// <param name="content">Content of the <c>.csproj</c>.</param>
    /// <param name="include">Value of <c>Include</c> with <c>\</c> separators, relative to the project, e.g. <c>..\..\Services\Billing\Billing.Infrastructure\Billing.Infrastructure.csproj</c>.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The project has no <c>ProjectReference</c> to insert next to.</exception>
    public static string AddReference(string content, string include)
    {
        var line = $"    <ProjectReference Include=\"{include}\" />";
        if (TextEdits.HasLine(content, existing => existing.Contains($"Include=\"{include}\"", StringComparison.OrdinalIgnoreCase)))
        {
            return content;
        }

        var lines = TextEdits.Lines(content);
        var references = lines.Select((text, index) => (text, index)).Where(item => item.text.TrimStart().StartsWith("<ProjectReference ", StringComparison.Ordinal)).ToList();
        if (references.Count == 0)
        {
            throw new ScaffoldException("The project file has no ProjectReference to insert next to; add the reference by hand.");
        }

        var next = references.FirstOrDefault(item => string.CompareOrdinal(item.text.Trim(), line.Trim()) > 0);
        lines.Insert(next.text is null ? references[^1].index + 1 : next.index, line);
        return TextEdits.Join(lines, content);
    }

    /// <summary>Removes the reference to a project file.</summary>
    /// <param name="content">Content of the <c>.csproj</c>.</param>
    /// <param name="projectFileName">File name of the referenced project, e.g. <c>Billing.Infrastructure.csproj</c>.</param>
    /// <returns>The new content.</returns>
    public static string RemoveReference(string content, string projectFileName) =>
        TextEdits.RemoveLines(content, line => line.TrimStart().StartsWith("<ProjectReference ", StringComparison.Ordinal)
            && line.Contains($"\\{projectFileName}\"", StringComparison.OrdinalIgnoreCase));
}
