using System.Text.RegularExpressions;

namespace SuperApp.Cli.Scaffolding;

/// <summary>Line-based edits of text files that keep their line endings: insert after or before an anchor line, remove lines.</summary>
/// <remarks>
/// The files the tool edits (compose, csproj, C#, SQL, Markdown) have a fixed layout, so an edit finds its place by a line it recognizes
/// (an anchor) instead of parsing the format. A missing anchor is a <see cref="ScaffoldException"/>: the file no longer has the layout
/// the tool knows, and a person has to make the change by hand.
/// </remarks>
internal static partial class TextEdits
{
    /// <summary>Splits text into lines without line terminators.</summary>
    /// <param name="content">The text.</param>
    /// <returns>The lines; the last one is empty when the text ends with a newline.</returns>
    public static List<string> Lines(string content) => [.. content.Split('\n').Select(line => line.TrimEnd('\r'))];

    /// <summary>Joins lines with the line ending used by <paramref name="original"/>.</summary>
    /// <param name="lines">The lines.</param>
    /// <param name="original">The original text, whose line ending (CRLF or LF) is kept.</param>
    /// <returns>The text.</returns>
    public static string Join(IEnumerable<string> lines, string original) =>
        string.Join(original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", lines);

    /// <summary>Inserts lines after the last line matching <paramref name="anchor"/>.</summary>
    /// <param name="content">The text.</param>
    /// <param name="anchor">Recognizes the line to insert after.</param>
    /// <param name="newLines">Lines to insert.</param>
    /// <param name="where">Name of the place for the error message, e.g. <c>the last AddDbContext</c>.</param>
    /// <returns>The changed text.</returns>
    /// <exception cref="ScaffoldException">No line matches.</exception>
    public static string InsertAfterLast(string content, Func<string, bool> anchor, IEnumerable<string> newLines, string where)
    {
        var lines = Lines(content);
        var index = lines.FindLastIndex(line => anchor(line));
        if (index < 0)
        {
            throw new ScaffoldException($"Cannot find {where}; make the change by hand.");
        }

        lines.InsertRange(index + 1, newLines);
        return Join(lines, content);
    }

    /// <summary>Inserts lines before the first line matching <paramref name="anchor"/>.</summary>
    /// <param name="content">The text.</param>
    /// <param name="anchor">Recognizes the line to insert before.</param>
    /// <param name="newLines">Lines to insert.</param>
    /// <param name="where">Name of the place for the error message.</param>
    /// <returns>The changed text.</returns>
    /// <exception cref="ScaffoldException">No line matches.</exception>
    public static string InsertBeforeFirst(string content, Func<string, bool> anchor, IEnumerable<string> newLines, string where)
    {
        var lines = Lines(content);
        var index = lines.FindIndex(line => anchor(line));
        if (index < 0)
        {
            throw new ScaffoldException($"Cannot find {where}; make the change by hand.");
        }

        lines.InsertRange(index, newLines);
        return Join(lines, content);
    }

    /// <summary>Removes every line matching <paramref name="predicate"/>.</summary>
    /// <param name="content">The text.</param>
    /// <param name="predicate">Recognizes the lines to remove.</param>
    /// <returns>The text without them (unchanged when none matches).</returns>
    public static string RemoveLines(string content, Func<string, bool> predicate) =>
        Join(Lines(content).Where(line => !predicate(line)), content);

    /// <summary>Whether a line is a namespace <c>using</c> directive (not a <c>using var</c> statement or an alias with generics).</summary>
    /// <param name="line">A line of C# code.</param>
    /// <returns><see langword="true"/> for lines such as <c>using SuperApp.Framework.Infrastructure.Hosting;</c>.</returns>
    public static bool IsUsingDirective(string line) => UsingDirective().IsMatch(line);

    /// <summary>Whether a line matches <paramref name="predicate"/>.</summary>
    /// <param name="content">The text.</param>
    /// <param name="predicate">The test.</param>
    /// <returns><see langword="true"/> when at least one line matches.</returns>
    public static bool HasLine(string content, Func<string, bool> predicate) => Lines(content).Any(predicate);

    [GeneratedRegex("^using (static )?[A-Za-z_][A-Za-z0-9_.]*;$")]
    private static partial Regex UsingDirective();
}
