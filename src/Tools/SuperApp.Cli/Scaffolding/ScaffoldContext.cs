using System.Text;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Scaffolding;

/// <summary>What a scaffolding step works with: the files of the repository and the runner of external commands.</summary>
/// <remarks>
/// File helpers keep the encoding of a file (UTF-8, with a BOM only when it had one) and report what they did, so every step returns
/// the same kind of <see cref="StepResult"/> and an unchanged file is reported as <see cref="StepStatus.Unchanged"/>.
/// </remarks>
/// <param name="files">Files of the repository.</param>
/// <param name="runner">Runner of <c>dotnet</c> commands in the repository root.</param>
internal sealed class ScaffoldContext(RepositoryFiles files, ProcessRunner runner)
{
    /// <summary>Files of the repository.</summary>
    public RepositoryFiles Files { get; } = files;

    /// <summary>Runner of <c>dotnet</c> commands in the repository root.</summary>
    public ProcessRunner Runner { get; } = runner;

    /// <summary>Changes an existing file with <paramref name="transform"/>.</summary>
    /// <param name="path">File relative to the repository root.</param>
    /// <param name="detail">What the change is, for the report.</param>
    /// <param name="transform">Returns the new content for the current one; returning it unchanged means nothing to do.</param>
    /// <returns><see cref="StepStatus.Changed"/> or <see cref="StepStatus.Unchanged"/>.</returns>
    /// <exception cref="ScaffoldException">The file does not exist.</exception>
    public StepResult Update(string path, string detail, Func<string, string> transform)
    {
        var full = Files.FullPath(path);
        if (!File.Exists(full))
        {
            throw new ScaffoldException($"{path} does not exist; the repository does not follow the expected layout.");
        }

        var bytes = File.ReadAllBytes(full);
        var hasBom = bytes is [0xEF, 0xBB, 0xBF, ..];
        var content = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        var updated = transform(content);
        if (updated == content)
        {
            return new StepResult(StepStatus.Unchanged, path, detail);
        }

        File.WriteAllText(full, updated, new UTF8Encoding(hasBom));
        return new StepResult(StepStatus.Changed, path, detail);
    }

    /// <summary>Creates a file, unless it already exists.</summary>
    /// <param name="path">File relative to the repository root.</param>
    /// <param name="content">Content of the new file.</param>
    /// <param name="detail">What the file is, for the report.</param>
    /// <returns><see cref="StepStatus.Created"/> or, for an existing file (left as it is), <see cref="StepStatus.Unchanged"/>.</returns>
    public StepResult Create(string path, string content, string detail)
    {
        var full = Files.FullPath(path);
        if (File.Exists(full))
        {
            return new StepResult(StepStatus.Unchanged, path, detail);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return new StepResult(StepStatus.Created, path, detail);
    }

    /// <summary>Deletes a file or a directory with everything in it, if it exists.</summary>
    /// <param name="path">File or directory relative to the repository root.</param>
    /// <param name="detail">What it is, for the report.</param>
    /// <returns><see cref="StepStatus.Deleted"/> or, when it did not exist, <see cref="StepStatus.Unchanged"/>.</returns>
    public StepResult Delete(string path, string detail)
    {
        var full = Files.FullPath(path);
        if (Directory.Exists(full))
        {
            Directory.Delete(full, recursive: true);
        }
        else if (File.Exists(full))
        {
            File.Delete(full);
        }
        else
        {
            return new StepResult(StepStatus.Unchanged, path, detail);
        }

        RemoveEmptyParents(Path.GetDirectoryName(full));
        return new StepResult(StepStatus.Deleted, path, detail);
    }

    // A folder left empty by a removal (e.g. Domain/Invoices after the last aggregate) goes too; never the root or a project directory.
    private void RemoveEmptyParents(string? directory)
    {
        var root = Path.GetFullPath(Files.Root).TrimEnd(Path.DirectorySeparatorChar);
        while (directory is not null
               && !string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(directory)
               && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }
}
