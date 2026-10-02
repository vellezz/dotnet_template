using System.Globalization;
using System.Text.RegularExpressions;

namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Gives a new component its range of 1000 log event IDs in <c>docs/logowanie-eventid.md</c> and takes it back on removal.</summary>
/// <remarks>
/// Free IDs are the pool row whose component starts with <c>kolejne</c> (e.g. <c>| 7000–8999 | kolejne serwisy i BFF-y … | — |</c>). A new
/// component gets the first thousand of the pool, as a row of its own right before it, and the pool shrinks (the last thousand replaces
/// the pool row). Removing the component gives its range back when it is adjacent to the pool, and recreates the pool when there is none,
/// so <c>add</c> followed by <c>remove</c> restores the register exactly, in any order of removal.
/// </remarks>
internal static partial class EventIdRegisterEditor
{
    // Text of a pool row recreated when the last component that used it up is removed.
    private const string PoolComponent = "kolejne serwisy i BFF-y (po 1000 na komponent)";

    /// <summary>Allocates the next range for a component.</summary>
    /// <param name="content">Content of the register.</param>
    /// <param name="component">Text of the component column, e.g. <c>Billing</c> or <c>`Coaching.Bff` (BFF experience Coaching)</c>.</param>
    /// <param name="range">The allocated range, e.g. <c>7000–7999</c>.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">There is no pool row or it has fewer than 1000 IDs.</exception>
    public static string Allocate(string content, string component, out string range)
    {
        var lines = TextEdits.Lines(content);
        var existing = lines.Select(line => Row().Match(line)).FirstOrDefault(row => row.Success && row.Groups[3].Value.Trim() == component);
        if (existing is not null)
        {
            range = $"{existing.Groups[1].Value}–{existing.Groups[2].Value}";
            return content;
        }

        var index = lines.FindIndex(line => Row().Match(line) is { Success: true } row && IsPool(row));
        if (index < 0)
        {
            throw new ScaffoldException("docs/logowanie-eventid.md has no pool row (component starting with \"kolejne\"); add a range by hand.");
        }

        var pool = Row().Match(lines[index]);
        var start = Number(pool.Groups[1].Value);
        var end = Number(pool.Groups[2].Value);
        if (end - start + 1 < 1000)
        {
            throw new ScaffoldException("The pool of event IDs in docs/logowanie-eventid.md is used up; extend it by hand.");
        }

        range = $"{start}–{start + 999}";
        var row = $"| {range} | {component} | — |";
        if (end - start + 1 == 1000)
        {
            lines[index] = row;
        }
        else
        {
            lines[index] = $"| {start + 1000}–{end} |{pool.Groups[3].Value}|{pool.Groups[4].Value}|";
            lines.Insert(index, row);
        }

        return TextEdits.Join(lines, content);
    }

    /// <summary>Removes the row of a component; a range adjacent to the pool goes back to it, and without a pool it becomes the pool.</summary>
    /// <param name="content">Content of the register.</param>
    /// <param name="component">Text of the component column, exactly as written by <see cref="Allocate"/>.</param>
    /// <returns>The new content.</returns>
    public static string Release(string content, string component)
    {
        var lines = TextEdits.Lines(content);
        var index = lines.FindIndex(line => Row().Match(line) is { Success: true } row && row.Groups[3].Value.Trim() == component);
        if (index < 0)
        {
            return content;
        }

        var released = Row().Match(lines[index]);
        var start = Number(released.Groups[1].Value);
        var end = Number(released.Groups[2].Value);
        lines.RemoveAt(index);

        var poolIndex = lines.FindIndex(line => Row().Match(line) is { Success: true } row && IsPool(row));
        if (poolIndex < 0)
        {
            // The removed component had taken the last free range: the range becomes the pool again.
            lines.Insert(index, $"| {start}–{end} | {PoolComponent} | — |");
            return TextEdits.Join(lines, content);
        }

        var pool = Row().Match(lines[poolIndex]);
        var poolStart = Number(pool.Groups[1].Value);
        var poolEnd = Number(pool.Groups[2].Value);
        if (end + 1 == poolStart || poolEnd + 1 == start)
        {
            // Adjacent to the pool (before or after it): the pool grows by the released range.
            lines[poolIndex] = $"| {Math.Min(start, poolStart)}–{Math.Max(end, poolEnd)} |{pool.Groups[3].Value}|{pool.Groups[4].Value}|";
        }

        return TextEdits.Join(lines, content);
    }

    private static bool IsPool(Match row) => row.Groups[3].Value.Trim().StartsWith("kolejne", StringComparison.OrdinalIgnoreCase);

    private static int Number(string text) => int.Parse(text, CultureInfo.InvariantCulture);

    [GeneratedRegex("^\\|\\s*(\\d+)\\s*[–-]\\s*(\\d+)\\s*\\|(.*)\\|([^|]*)\\|\\s*$")]
    private static partial Regex Row();
}
