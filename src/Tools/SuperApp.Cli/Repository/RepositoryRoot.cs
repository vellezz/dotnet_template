namespace SuperApp.Cli.Repository;

/// <summary>Finds the root directory of the repository: the directory that contains <c>SuperApp.slnx</c>.</summary>
/// <remarks>
/// Every command works on the repository as a whole, wherever in it the tool is started. Without <c>--root</c> the search starts in the
/// current directory and goes up; the first directory with <c>SuperApp.slnx</c> wins.
/// </remarks>
internal static class RepositoryRoot
{
    /// <summary>Name of the solution file that marks the repository root.</summary>
    public const string MarkerFile = "SuperApp.slnx";

    /// <summary>Returns the repository root for <paramref name="start"/>, or <see langword="null"/> when no parent contains the marker.</summary>
    /// <param name="start">Directory to start from: the <c>--root</c> option or the current directory.</param>
    /// <returns>Full path of the root, or <see langword="null"/>.</returns>
    public static string? Find(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, MarkerFile)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
