namespace SuperApp.Cli.Repository;

/// <summary>Reads files of the repository by paths relative to its root, skipping build output and IDE folders.</summary>
/// <remarks>
/// Paths in findings and command output are always relative to the root and use <c>/</c>, so they are the same on Windows and Linux and
/// can be pasted into the documentation and into Copilot prompts as they are.
/// </remarks>
/// <param name="root">Full path of the repository root.</param>
internal sealed class RepositoryFiles(string root)
{
    /// <summary>
    /// Directory of the <c>dotnet new</c> templates used by <c>add service</c> and <c>add bff</c>. Its projects, launch profiles and log
    /// event IDs are placeholders (<c>ServiceName</c>, ports 5190–5192), so the scanner never lists it.
    /// </summary>
    public const string TemplatesDirectory = "src/Tools/SuperApp.Cli/Templates";

    private static readonly HashSet<string> SkippedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", ".idea", "node_modules", "TestResults", ".playwright-mcp" };

    /// <summary>Full path of the repository root.</summary>
    public string Root { get; } = root;

    /// <summary>Returns the full path of <paramref name="relativePath"/>.</summary>
    /// <param name="relativePath">Path relative to the root, with <c>/</c> or <c>\</c>.</param>
    /// <returns>The full path; the file does not have to exist.</returns>
    public string FullPath(string relativePath) => Path.GetFullPath(Path.Combine(Root, relativePath.Replace('\\', '/')));

    /// <summary>Returns <paramref name="fullPath"/> relative to the root, with <c>/</c> separators.</summary>
    /// <param name="fullPath">A path inside the repository.</param>
    /// <returns>The relative path.</returns>
    public string Relative(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    /// <summary>Whether the file exists.</summary>
    /// <param name="relativePath">Path relative to the root.</param>
    /// <returns><see langword="true"/> for an existing file.</returns>
    public bool Exists(string relativePath) => File.Exists(FullPath(relativePath));

    /// <summary>Reads the whole file, or returns an empty string when it does not exist.</summary>
    /// <param name="relativePath">Path relative to the root.</param>
    /// <returns>The content, or an empty string.</returns>
    public string ReadOrEmpty(string relativePath) => Exists(relativePath) ? File.ReadAllText(FullPath(relativePath)) : string.Empty;

    /// <summary>Lists subdirectories of a directory, or nothing when it does not exist.</summary>
    /// <param name="relativePath">Directory relative to the root.</param>
    /// <returns>Names of the subdirectories, sorted.</returns>
    public IReadOnlyList<string> Directories(string relativePath)
    {
        var full = FullPath(relativePath);
        return Directory.Exists(full)
            ? [.. Directory.GetDirectories(full).Where(directory => !IsSkipped(directory)).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>Enumerates files below a directory, skipping build output and IDE folders.</summary>
    /// <param name="relativePath">Directory relative to the root; an empty string for the root.</param>
    /// <param name="pattern">File name pattern, e.g. <c>*.cs</c>.</param>
    /// <returns>Paths relative to the root, sorted.</returns>
    public IReadOnlyList<string> Files(string relativePath, string pattern)
    {
        var start = FullPath(relativePath);
        if (!Directory.Exists(start))
        {
            return [];
        }

        var result = new List<string>();
        var pending = new Stack<string>([start]);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            result.AddRange(Directory.GetFiles(directory, pattern).Select(Relative));
            foreach (var child in Directory.GetDirectories(directory))
            {
                if (!IsSkipped(child))
                {
                    pending.Push(child);
                }
            }
        }

        return [.. result.Order(StringComparer.Ordinal)];
    }

    private bool IsSkipped(string directory) =>
        SkippedDirectories.Contains(Path.GetFileName(directory)) || Relative(directory) == TemplatesDirectory;
}
