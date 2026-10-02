using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding;

namespace SuperApp.Cli.Contracts;

/// <summary>A set of OpenAPI contracts keyed by their path relative to the repository root: the committed ones or a previous version of them.</summary>
/// <remarks>
/// <para>
/// A contract is every <c>openapi/*.json</c> under <c>src</c> (the Api of a service, a BFF). The previous version comes from one of two
/// places, so the comparison works also in a copy of the repository without git:
/// </para>
/// <list type="bullet">
///   <item><description>a snapshot directory written by <c>dotnet superapp contracts snapshot</c> (<see cref="FromDirectory"/>): the same relative
///   paths under the directory, by default <see cref="DefaultDirectory"/>;</description></item>
///   <item><description>a git revision (<see cref="FromGit"/>): the contracts as committed in a branch, tag or commit, e.g. <c>origin/main</c>.</description></item>
/// </list>
/// </remarks>
/// <param name="contracts">Content of every contract by its path relative to the repository root.</param>
/// <param name="source">Where the set comes from, for the report.</param>
internal sealed class ContractBaseline(IReadOnlyDictionary<string, string> contracts, string source)
{
    /// <summary>Snapshot directory used when none is given: <c>artifacts/</c> is ignored by the repository, the snapshot is local.</summary>
    public const string DefaultDirectory = "artifacts/contracts-baseline";

    /// <summary>Content of every contract by its path relative to the repository root, with forward slashes.</summary>
    public IReadOnlyDictionary<string, string> Contracts { get; } = contracts;

    /// <summary>Where the set comes from: the working tree, a directory or a git revision.</summary>
    public string Source { get; } = source;

    /// <summary>Reads the contracts of the working tree.</summary>
    /// <param name="files">Repository files.</param>
    /// <returns>The current contracts.</returns>
    public static ContractBaseline Current(RepositoryFiles files) =>
        new(Paths(files.Files("src", "*.json")).ToDictionary(path => path, files.ReadOrEmpty, StringComparer.Ordinal), "working tree");

    /// <summary>Reads a snapshot written by <c>contracts snapshot</c>.</summary>
    /// <param name="directory">Absolute path of the snapshot directory.</param>
    /// <returns>The contracts of the snapshot, or <see langword="null"/> when the directory does not exist.</returns>
    public static ContractBaseline? FromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var contracts = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Select(path => (Relative: Path.GetRelativePath(directory, path).Replace('\\', '/'), Full: path))
            .Where(file => IsContract(file.Relative))
            .ToDictionary(file => file.Relative, file => File.ReadAllText(file.Full), StringComparer.Ordinal);
        return new ContractBaseline(contracts, directory);
    }

    /// <summary>Reads the contracts committed in a git revision.</summary>
    /// <param name="runner">Runs git in the repository root.</param>
    /// <param name="revision">Branch, tag or commit, e.g. <c>origin/main</c> or <c>HEAD~1</c>.</param>
    /// <returns>The contracts of the revision.</returns>
    /// <exception cref="ScaffoldException">Git is missing, the root is not a git repository or the revision does not exist.</exception>
    public static ContractBaseline FromGit(ProcessRunner runner, string revision)
    {
        var listing = runner.Git("ls-tree", "-r", "--name-only", revision, "--", "src");
        var contracts = Paths(listing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToDictionary(path => path, path => runner.Git("show", $"{revision}:{path}"), StringComparer.Ordinal);
        return new ContractBaseline(contracts, $"git {revision}");
    }

    /// <summary>Writes the set as a snapshot directory, replacing a previous snapshot.</summary>
    /// <param name="directory">Absolute path of the snapshot directory.</param>
    public void WriteTo(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        foreach (var (path, content) in Contracts)
        {
            var target = Path.Combine(directory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }
    }

    /// <summary>Compares every contract of this baseline with its current version.</summary>
    /// <param name="current">The current contracts.</param>
    /// <returns>The differences of all contracts, breaking first; a removed contract is breaking, a new one compatible.</returns>
    public IReadOnlyList<ContractChange> CompareWith(ContractBaseline current)
    {
        var changes = new List<ContractChange>();
        foreach (var (path, baseline) in Contracts)
        {
            changes.AddRange(current.Contracts.TryGetValue(path, out var content)
                ? OpenApiDiff.Compare(path, baseline, content)
                : [new ContractChange(ContractChangeKind.Breaking, path, "(contract)", "contract removed")]);
        }

        changes.AddRange(current.Contracts.Keys.Where(path => !Contracts.ContainsKey(path))
            .Select(path => new ContractChange(ContractChangeKind.Compatible, path, "(contract)", "contract added")));
        return [.. changes.OrderByDescending(change => change.Kind).ThenBy(change => change.Contract, StringComparer.Ordinal)];
    }

    private static IEnumerable<string> Paths(IEnumerable<string> paths) => paths.Select(path => path.Replace('\\', '/')).Where(IsContract);

    private static bool IsContract(string path) =>
        path.Contains("/openapi/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal)
        && !path.StartsWith(RepositoryFiles.TemplatesDirectory, StringComparison.Ordinal);
}
