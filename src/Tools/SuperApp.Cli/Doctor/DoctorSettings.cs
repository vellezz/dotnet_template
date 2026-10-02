using System.Text.Json;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor;

/// <summary>Repository-specific exceptions for <c>doctor</c>, read from <c>.config/superapp-doctor.json</c>.</summary>
/// <remarks>
/// Keeps the rules generic: names that the documentation mentions on purpose without the file existing (files a recipe tells the reader
/// to create, e.g. <c>values-billing.yaml</c>) are listed here instead of in the code. Every entry should say in the file why it is there.
/// A missing file means no exceptions.
/// </remarks>
/// <param name="IgnoredPathFragments">
/// Fragments of repository paths in the documentation that are not checked for existence, e.g. <c>{</c> for placeholders or
/// <c>Billing</c> for the example service of a recipe.
/// </param>
internal sealed record DoctorSettings(IReadOnlyList<string> IgnoredPathFragments)
{
    /// <summary>Path of the settings file relative to the repository root.</summary>
    public const string Path = ".config/superapp-doctor.json";

    /// <summary>Reads the settings of the repository, or empty settings when the file does not exist.</summary>
    /// <param name="files">Files of the repository.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    public static DoctorSettings Load(RepositoryFiles files)
    {
        var content = files.ReadOrEmpty(Path);
        if (content.Length == 0)
        {
            return new DoctorSettings([]);
        }

        using var document = JsonDocument.Parse(content, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var fragments = document.RootElement.TryGetProperty("ignoredPathFragments", out var list)
            ? list.EnumerateArray().Select(item => item.GetString()).OfType<string>().ToList()
            : [];
        return new DoctorSettings(fragments);
    }
}
