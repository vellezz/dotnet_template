using System.Text.RegularExpressions;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor.Rules;

/// <summary>Repository paths written as code in the documentation (<c>`src/…`</c>, <c>`deploy/…`</c>) exist.</summary>
/// <remarks>
/// The guide and the ADRs point to concrete files as examples to follow; after a rename or a move these paths go stale without any link
/// to break. Only paths starting with a top-level directory of the repository are checked. Paths with placeholders (<c>{Service}</c>,
/// <c>*</c>, <c>…</c>) and paths containing a fragment from <see cref="DoctorSettings.IgnoredPathFragments"/> (files a recipe tells the
/// reader to create) are skipped; a <c>:line</c> suffix is ignored.
/// </remarks>
internal sealed partial class DocumentationPathsRule : IDoctorRule
{
    private static readonly string[] Roots = ["src/", "tests/", "deploy/", "docs/", ".github/", ".config/", "tools/"];
    private static readonly string[] Placeholders = ["{", "}", "*", "<", ">", "...", "…", "$"];

    /// <inheritdoc />
    public string Id => "doc-paths";

    /// <inheritdoc />
    public string Description => "Repository paths quoted in the documentation exist (placeholders and configured exceptions are skipped).";

    /// <inheritdoc />
    public IEnumerable<Finding> Check(RepositoryModel model, DoctorSettings settings)
    {
        var files = model.Files;
        foreach (var document in MarkdownFiles.All(files))
        {
            var content = files.ReadOrEmpty(document);
            foreach (Match code in InlineCode().Matches(content))
            {
                var path = code.Groups[1].Value.Trim().TrimEnd('.', ',', ';', ':', ')');
                if (LineSuffix().Match(path) is { Success: true } suffix)
                {
                    path = path[..suffix.Index];
                }

                if (!Roots.Any(root => path.StartsWith(root, StringComparison.Ordinal))
                    || Placeholders.Any(token => path.Contains(token, StringComparison.Ordinal))
                    || settings.IgnoredPathFragments.Any(fragment => path.Contains(fragment, StringComparison.Ordinal)))
                {
                    continue;
                }

                var full = files.FullPath(path);
                if (!File.Exists(full) && !Directory.Exists(full))
                {
                    yield return new Finding(Id, Severity.Error, $"Path {path} does not exist.", document, content.AsSpan(0, code.Index).Count('\n') + 1,
                        $"Fix the path; if the file is meant to be created by the reader, add a fragment of it to {DoctorSettings.Path}.");
                }
            }
        }
    }

    [GeneratedRegex("`([^`\\s]+)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(":\\d+$")]
    private static partial Regex LineSuffix();
}
