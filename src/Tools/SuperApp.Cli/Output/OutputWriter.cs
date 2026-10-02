using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuperApp.Cli.Output;

/// <summary>Writes command results as aligned text tables or, with <c>--json</c>, as one JSON document.</summary>
/// <remarks>
/// In JSON mode a command writes exactly one document to the output and nothing else, so it can be piped to <c>jq</c> or read by an
/// assistant; errors go to the error writer in both modes. Property names are camelCase and enums are strings.
/// </remarks>
/// <param name="output">Writer of the results.</param>
/// <param name="error">Writer of error messages.</param>
/// <param name="json">Whether to write JSON instead of text.</param>
internal sealed class OutputWriter(TextWriter output, TextWriter error, bool json)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Whether the command should write JSON.</summary>
    public bool IsJson { get; } = json;

    /// <summary>Writes <paramref name="value"/> as a JSON document.</summary>
    /// <param name="value">The result object of the command.</param>
    public void WriteJson(object value) => output.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    /// <summary>Writes one line of text.</summary>
    /// <param name="line">The text; an empty line by default.</param>
    public void Line(string line = "") => output.WriteLine(line);

    /// <summary>Writes an error message to the error writer.</summary>
    /// <param name="message">The message.</param>
    public void Error(string message) => error.WriteLine(message);

    /// <summary>Writes a table with a header row and columns padded to the widest cell.</summary>
    /// <param name="headers">Column headers.</param>
    /// <param name="rows">Rows; each must have as many cells as <paramref name="headers"/>.</param>
    public void Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var all = rows.ToList();
        var widths = headers.Select((header, column) => all.Select(row => row[column].Length).Append(header.Length).Max()).ToList();

        string Format(IReadOnlyList<string> cells) =>
            string.Join("  ", cells.Select((cell, column) => column == cells.Count - 1 ? cell : cell.PadRight(widths[column]))).TrimEnd();

        output.WriteLine(Format(headers));
        output.WriteLine(Format([.. widths.Select(width => new string('-', width))]));
        foreach (var row in all)
        {
            output.WriteLine(Format(row));
        }
    }
}
