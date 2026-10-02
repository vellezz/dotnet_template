using System.Globalization;
using System.Text.RegularExpressions;

namespace SuperApp.Cli.Repository;

/// <summary>Reads the EventId register <c>docs/logowanie-eventid.md</c>: one table row per component range (ADR-0008).</summary>
/// <remarks>
/// Rows look like <c>| 3000–3999 | `SuperApp.Gateway`: … | 3001–3004, 3101–3104 |</c>. The first column is the range, the second the
/// component, the third the IDs in use (single IDs and ranges, or <c>—</c>). Both the en dash and the hyphen are accepted.
/// </remarks>
internal static partial class EventIdRegister
{
    /// <summary>Path of the register relative to the repository root.</summary>
    public const string Path = "docs/logowanie-eventid.md";

    /// <summary>Parses the ranges of the register.</summary>
    /// <param name="content">Content of the file; empty when it does not exist.</param>
    /// <returns>The ranges in file order.</returns>
    public static IReadOnlyList<EventIdRange> Read(string content)
    {
        var ranges = new List<EventIdRange>();
        foreach (var line in content.Split('\n'))
        {
            var row = Row().Match(line.TrimEnd('\r'));
            if (!row.Success)
            {
                continue;
            }

            var documented = new HashSet<int>();
            foreach (Match item in UsedItem().Matches(row.Groups[4].Value))
            {
                var first = Number(item.Groups[1].Value);
                var last = item.Groups[2].Success ? Number(item.Groups[2].Value) : first;
                for (var id = first; id <= last; id++)
                {
                    documented.Add(id);
                }
            }

            ranges.Add(new EventIdRange(Number(row.Groups[1].Value), Number(row.Groups[2].Value), row.Groups[3].Value.Trim(), documented));
        }

        return ranges;
    }

    private static int Number(string text) => int.Parse(text, CultureInfo.InvariantCulture);

    [GeneratedRegex("^\\|\\s*(\\d+)\\s*[–-]\\s*(\\d+)\\s*\\|(.*)\\|([^|]*)\\|\\s*$")]
    private static partial Regex Row();

    [GeneratedRegex("(\\d+)(?:\\s*[–-]\\s*(\\d+))?")]
    private static partial Regex UsedItem();
}
