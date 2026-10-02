using System.Text.RegularExpressions;

namespace SuperApp.ArchitectureTests;

/// <summary>
/// <c>SuperApp.slnx</c> (CLI, Visual Studio, VS Code) and <c>SuperApp.sln</c> (IDEs without full <c>.slnx</c> support, e.g. Rider) list the
/// same projects in the same solution folders, so a project added to only one of them, or placed in another folder, is caught.
/// </summary>
public sealed partial class SolutionFilesTests
{
    [Fact]
    public void Slnx_and_sln_list_the_same_projects_in_the_same_folders()
    {
        var root = RepositoryRoot();
        var slnx = SlnxProjects(File.ReadAllText(Path.Combine(root, "SuperApp.slnx")));
        var sln = SlnProjects(File.ReadAllText(Path.Combine(root, "SuperApp.sln")));

        Assert.NotEmpty(slnx);
        Assert.Equal(slnx, sln);
    }

    // "folder/path | project/path.csproj" for every project of the .slnx file, sorted.
    private static List<string> SlnxProjects(string slnx)
    {
        var entries = new List<string>();
        var folder = string.Empty;
        foreach (var line in slnx.Split('\n'))
        {
            var folderMatch = SlnxFolder().Match(line);
            if (folderMatch.Success)
            {
                folder = folderMatch.Groups[1].Value.Trim('/');
            }

            var projectMatch = SlnxProject().Match(line);
            if (projectMatch.Success)
            {
                entries.Add($"{folder} | {projectMatch.Groups[1].Value.Replace('\\', '/')}");
            }
        }

        return [.. entries.Order(StringComparer.Ordinal)];
    }

    // The same for the .sln file: the folder path is rebuilt from the NestedProjects section.
    private static List<string> SlnProjects(string sln)
    {
        var items = SlnItem().Matches(sln).ToDictionary(match => match.Groups[3].Value, match => (Name: match.Groups[1].Value, Path: match.Groups[2].Value));
        var nested = SlnNested().Match(sln).Groups[1].Value;
        var parents = SlnParent().Matches(nested).ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);

        string FolderOf(string id) =>
            parents.TryGetValue(id, out var parent) ? FolderPath(parent) : string.Empty;

        string FolderPath(string id) =>
            parents.TryGetValue(id, out var parent) ? $"{FolderPath(parent)}/{items[id].Name}" : items[id].Name;

        return [.. items
            .Where(item => item.Value.Path.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(item => $"{FolderOf(item.Key)} | {item.Value.Path.Replace('\\', '/')}")
            .Order(StringComparer.Ordinal)];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SuperApp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("SuperApp.slnx not found above the test output directory.");
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
