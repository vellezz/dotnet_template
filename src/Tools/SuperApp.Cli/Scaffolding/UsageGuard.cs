using System.Text.RegularExpressions;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Scaffolding;

/// <summary>Refuses a removal while code outside the removed element still uses it.</summary>
/// <remarks>
/// <c>remove aggregate|event|consumer|scope|flag|product-event</c> deletes the files <c>add</c> created. Any other file that mentions one of
/// the element's names (a handler using the repository, a consumer of the contract, a migration snapshot with the table, a test) would
/// stop compiling or lose meaning, so the removal stops and lists those files: the person decides what happens to them.
/// </remarks>
internal static class UsageGuard
{
    /// <summary>Throws when a C# file in <paramref name="directories"/>, other than <paramref name="ownFiles"/>, mentions one of the names.</summary>
    /// <param name="files">Files of the repository.</param>
    /// <param name="directories">Directories to search, relative to the repository root.</param>
    /// <param name="names">Whole-word names, e.g. <c>Invoice</c>, <c>InvoiceId</c>, <c>IInvoiceRepository</c>.</param>
    /// <param name="ownFiles">Files that belong to the element and are removed with it.</param>
    /// <param name="what">The element, for the message.</param>
    /// <exception cref="ScaffoldException">At least one other file uses the element.</exception>
    public static void EnsureUnused(RepositoryFiles files, IEnumerable<string> directories, IReadOnlyCollection<string> names, IReadOnlyCollection<string> ownFiles, string what)
    {
        var pattern = new Regex($"\\b({string.Join('|', names.Select(Regex.Escape))})\\b", RegexOptions.None, TimeSpan.FromSeconds(1));
        var users = directories
            .SelectMany(directory => files.Files(directory, "*.cs"))
            .Distinct(StringComparer.Ordinal)
            .Where(file => !ownFiles.Contains(file) && pattern.IsMatch(WithoutComments(files.ReadOrEmpty(file))))
            .ToList();
        if (users.Count > 0)
        {
            throw new ScaffoldException($"{what} is still used by: {string.Join(", ", users)}. Remove or change that code first; a table or column that exists in a migration needs a contract migration (chapter 07).");
        }
    }

    // Names in comments and XML documentation ("Note:", <see cref="..."/>) are not usages that break the build.
    private static string WithoutComments(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && !line.TrimStart().StartsWith('*')));
}
