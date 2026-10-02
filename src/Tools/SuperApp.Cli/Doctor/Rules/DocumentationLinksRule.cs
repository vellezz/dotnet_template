using System.Text.RegularExpressions;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor.Rules;

/// <summary>Relative links in the documentation point to existing files, and links with <c>#anchor</c> to existing headings.</summary>
/// <remarks>
/// Renaming a file or a heading silently breaks links in other chapters of the guide, ADRs and Copilot instructions. External links
/// (<c>http</c>, <c>mailto</c>) are not checked; links inside fenced code blocks are ignored.
/// </remarks>
internal sealed partial class DocumentationLinksRule : IDoctorRule
{
    /// <inheritdoc />
    public string Id => "doc-links";

    /// <inheritdoc />
    public string Description => "Relative links and #anchors in docs/, .github/ and the Markdown files of the root resolve.";

    /// <inheritdoc />
    public IEnumerable<Finding> Check(RepositoryModel model, DoctorSettings settings)
    {
        var files = model.Files;
        var anchorCache = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var document in MarkdownFiles.All(files))
        {
            var content = MarkdownFiles.WithoutCodeBlocks(files.ReadOrEmpty(document));
            var directory = Path.GetDirectoryName(document) ?? string.Empty;
            foreach (Match link in Link().Matches(content))
            {
                var target = link.Groups[1].Value;
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hash = target.IndexOf('#', StringComparison.Ordinal);
                var path = hash < 0 ? target : target[..hash];
                var anchor = hash < 0 ? null : target[(hash + 1)..];
                var resolved = path.Length == 0 ? document : files.Relative(files.FullPath(Path.Combine(directory, Uri.UnescapeDataString(path))));
                var full = files.FullPath(resolved);
                var line = content.AsSpan(0, link.Index).Count('\n') + 1;

                if (!File.Exists(full) && !Directory.Exists(full))
                {
                    yield return new Finding(Id, Severity.Error, $"Link {target} points to {resolved}, which does not exist.", document, line,
                        "Fix the path, or the link of the renamed file.");
                }
                else if (anchor is { Length: > 0 } && resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    if (!anchorCache.TryGetValue(resolved, out var anchors))
                    {
                        anchors = anchorCache[resolved] = MarkdownFiles.Anchors(files.ReadOrEmpty(resolved));
                    }

                    if (!anchors.Contains(anchor.ToLowerInvariant()))
                    {
                        yield return new Finding(Id, Severity.Error, $"Link {target} points to a heading that {resolved} does not have.", document, line,
                            "Use the anchor of the current heading (lower case, punctuation removed, spaces as '-').");
                    }
                }
            }
        }
    }

    [GeneratedRegex("\\]\\(([^)\\s]+)\\)")]
    private static partial Regex Link();
}
