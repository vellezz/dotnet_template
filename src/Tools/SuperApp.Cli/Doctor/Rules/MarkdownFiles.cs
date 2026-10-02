using System.Text;
using System.Text.RegularExpressions;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor.Rules;

/// <summary>Markdown helpers shared by the documentation rules: which files are documentation and how GitHub names heading anchors.</summary>
internal static partial class MarkdownFiles
{
    /// <summary>Documentation files of the repository: <c>docs/</c>, <c>.github/</c> and the Markdown files of the repository root.</summary>
    /// <param name="files">Files of the repository.</param>
    /// <returns>Paths relative to the root.</returns>
    public static IReadOnlyList<string> All(RepositoryFiles files) =>
        [.. files.Files("docs", "*.md").Concat(files.Files(".github", "*.md"))
            .Concat(Directory.EnumerateFiles(files.Root, "*.md").Order(StringComparer.Ordinal).Select(files.Relative))];

    /// <summary>Removes fenced code blocks, whose content is code and not links or prose.</summary>
    /// <param name="markdown">Content of a Markdown file.</param>
    /// <returns>The content without the blocks.</returns>
    public static string WithoutCodeBlocks(string markdown) => CodeBlock().Replace(markdown, string.Empty);

    /// <summary>Returns the anchors GitHub creates for the headings of a file, including the <c>-1</c>, <c>-2</c> suffixes of repeated headings.</summary>
    /// <param name="markdown">Content of a Markdown file.</param>
    /// <returns>The anchors, lower case.</returns>
    public static IReadOnlySet<string> Anchors(string markdown)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var inFence = false;
        foreach (var line in markdown.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            var heading = Heading().Match(line.TrimEnd('\r'));
            if (inFence || !heading.Success)
            {
                continue;
            }

            var slug = Slug(heading.Groups[1].Value);
            if (counts.TryGetValue(slug, out var count))
            {
                counts[slug] = count + 1;
                anchors.Add($"{slug}-{count + 1}");
            }
            else
            {
                counts[slug] = 0;
                anchors.Add(slug);
            }
        }

        return anchors;
    }

    // GitHub: lower case, punctuation removed (letters of any alphabet, digits, '-' and '_' kept), spaces become '-'.
    private static string Slug(string heading)
    {
        var text = new StringBuilder();
        foreach (var character in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                text.Append(character);
            }
            else if (character == ' ')
            {
                text.Append('-');
            }
        }

        return text.ToString();
    }

    [GeneratedRegex("```.*?```", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex("^#{1,6}\\s+(.*)$")]
    private static partial Regex Heading();
}
