using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SuperApp.Cli.Repository;

/// <summary>
/// Reads the two solution files of the repository and writes <c>SuperApp.sln</c> from <c>SuperApp.slnx</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>SuperApp.slnx</c> is the source of truth (CLI, CI, Visual Studio, VS Code). <c>SuperApp.sln</c> exists for IDEs without full
/// <c>.slnx</c> support (Rider) and must list the same projects in the same solution folders. Generating it is safer than editing it with
/// <c>dotnet sln add</c>, which also adds referenced projects and puts them into whatever folder was given.
/// </para>
/// <para>
/// GUIDs in the generated file are derived from the folder or project path, so regenerating an unchanged solution gives an identical file.
/// </para>
/// </remarks>
internal static partial class SolutionFiles
{
    /// <summary>File name of the XML solution.</summary>
    public const string Slnx = "SuperApp.slnx";

    /// <summary>File name of the classic solution.</summary>
    public const string Sln = "SuperApp.sln";

    private const string CSharpProjectType = "9A19103F-16F7-4668-BE54-9A1E7A4F7556";
    private const string FolderType = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    /// <summary>Reads the projects of an <c>.slnx</c> file with their folders.</summary>
    /// <param name="content">Content of the file.</param>
    /// <returns>The entries in file order.</returns>
    public static IReadOnlyList<SolutionEntry> ReadSlnx(string content)
    {
        var entries = new List<SolutionEntry>();
        var folder = string.Empty;
        foreach (var line in content.Split('\n'))
        {
            var folderMatch = SlnxFolder().Match(line);
            if (folderMatch.Success)
            {
                folder = folderMatch.Groups[1].Value.Trim('/');
            }

            var projectMatch = SlnxProject().Match(line);
            if (projectMatch.Success)
            {
                entries.Add(new SolutionEntry(folder, projectMatch.Groups[1].Value.Replace('\\', '/')));
            }
        }

        return entries;
    }

    /// <summary>Reads the projects of an <c>.sln</c> file with their folders, rebuilt from the <c>NestedProjects</c> section.</summary>
    /// <param name="content">Content of the file.</param>
    /// <returns>The entries, sorted by project path.</returns>
    public static IReadOnlyList<SolutionEntry> ReadSln(string content)
    {
        var items = SlnItem().Matches(content).ToDictionary(match => match.Groups[3].Value.ToUpperInvariant(), match => (Name: match.Groups[1].Value, Path: match.Groups[2].Value));
        var parents = SlnParent().Matches(SlnNested().Match(content).Groups[1].Value)
            .ToDictionary(match => match.Groups[1].Value.ToUpperInvariant(), match => match.Groups[2].Value.ToUpperInvariant());

        string FolderPath(string id) =>
            parents.TryGetValue(id, out var parent) ? $"{FolderPath(parent)}/{items[id].Name}" : items[id].Name;

        return [.. items
            .Where(item => item.Value.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(item => new SolutionEntry(parents.TryGetValue(item.Key, out var parent) ? FolderPath(parent) : string.Empty, item.Value.Path.Replace('\\', '/')))
            .OrderBy(entry => entry.ProjectPath, StringComparer.Ordinal)];
    }

    /// <summary>Writes the content of an <c>.sln</c> file with the given projects and folders.</summary>
    /// <param name="entries">The projects of the <c>.slnx</c> file.</param>
    /// <returns>The file content with CRLF line endings, as Visual Studio and Rider write it.</returns>
    public static string WriteSln(IReadOnlyList<SolutionEntry> entries)
    {
        var folders = entries
            .SelectMany(entry => Prefixes(entry.Folder))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var text = new StringBuilder();
        text.Append("\r\nMicrosoft Visual Studio Solution File, Format Version 12.00\r\n# Visual Studio Version 17\r\n");
        text.Append("VisualStudioVersion = 17.0.31903.59\r\nMinimumVisualStudioVersion = 10.0.40219.1\r\n");
        foreach (var folder in folders)
        {
            var name = folder[(folder.LastIndexOf('/') + 1)..];
            text.Append($"Project(\"{{{FolderType}}}\") = \"{name}\", \"{name}\", \"{{{Guid("folder:" + folder)}}}\"\r\nEndProject\r\n");
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileNameWithoutExtension(entry.ProjectPath);
            text.Append($"Project(\"{{{CSharpProjectType}}}\") = \"{name}\", \"{entry.ProjectPath.Replace('/', '\\')}\", \"{{{Guid("project:" + entry.ProjectPath)}}}\"\r\nEndProject\r\n");
        }

        text.Append("Global\r\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n");
        text.Append("\t\tDebug|Any CPU = Debug|Any CPU\r\n\t\tRelease|Any CPU = Release|Any CPU\r\n\tEndGlobalSection\r\n");
        text.Append("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n");
        foreach (var entry in entries)
        {
            var id = Guid("project:" + entry.ProjectPath);
            foreach (var configuration in (string[])["Debug", "Release"])
            {
                text.Append($"\t\t{{{id}}}.{configuration}|Any CPU.ActiveCfg = {configuration}|Any CPU\r\n");
                text.Append($"\t\t{{{id}}}.{configuration}|Any CPU.Build.0 = {configuration}|Any CPU\r\n");
            }
        }

        text.Append("\tEndGlobalSection\r\n\tGlobalSection(SolutionProperties) = preSolution\r\n\t\tHideSolutionNode = FALSE\r\n\tEndGlobalSection\r\n");
        text.Append("\tGlobalSection(NestedProjects) = preSolution\r\n");
        foreach (var folder in folders.Where(folder => folder.Contains('/', StringComparison.Ordinal)))
        {
            text.Append($"\t\t{{{Guid("folder:" + folder)}}} = {{{Guid("folder:" + folder[..folder.LastIndexOf('/')])}}}\r\n");
        }

        foreach (var entry in entries.Where(entry => entry.Folder.Length > 0))
        {
            text.Append($"\t\t{{{Guid("project:" + entry.ProjectPath)}}} = {{{Guid("folder:" + entry.Folder)}}}\r\n");
        }

        text.Append("\tEndGlobalSection\r\nEndGlobal\r\n");
        return text.ToString();
    }

    private static IEnumerable<string> Prefixes(string folder)
    {
        if (folder.Length == 0)
        {
            yield break;
        }

        var parts = folder.Split('/');
        for (var length = 1; length <= parts.Length; length++)
        {
            yield return string.Join('/', parts.Take(length));
        }
    }

    // Name-based GUID (SHA-256 of the key, version 5 layout): stable across runs and machines.
    private static string Guid(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true).ToString().ToUpperInvariant();
    }

    [GeneratedRegex("<Folder Name=\"([^\"]+)\"")]
    private static partial Regex SlnxFolder();

    [GeneratedRegex("<Project Path=\"([^\"]+)\"")]
    private static partial Regex SlnxProject();

    [GeneratedRegex("^Project\\(\"\\{[0-9A-Fa-f-]+\\}\"\\) = \"([^\"]+)\", \"([^\"]+)\", \"\\{([0-9A-Fa-f-]+)\\}\"", RegexOptions.Multiline)]
    private static partial Regex SlnItem();

    [GeneratedRegex("GlobalSection\\(NestedProjects\\) = preSolution(.*?)EndGlobalSection", RegexOptions.Singleline)]
    private static partial Regex SlnNested();

    [GeneratedRegex("\\{([0-9A-Fa-f-]+)\\} = \\{([0-9A-Fa-f-]+)\\}")]
    private static partial Regex SlnParent();
}
