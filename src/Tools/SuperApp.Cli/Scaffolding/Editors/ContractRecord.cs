using System.Text.RegularExpressions;

namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>An integration event record read from the source of a <c>{Service}.Contracts</c> project: its name and positional parameters.</summary>
/// <param name="Name">Type name, e.g. <c>SleepEntryRecordedV1</c>.</param>
/// <param name="Parameters">Parameter types and names in declaration order, e.g. <c>(Guid, EntryId)</c>.</param>
internal sealed partial record ContractRecord(string Name, IReadOnlyList<(string Type, string Name)> Parameters)
{
    /// <summary>Reads a positional record declaration from source code.</summary>
    /// <param name="source">Content of the contract file.</param>
    /// <param name="name">Type name to find.</param>
    /// <returns>The record, or <see langword="null"/> when the file does not declare it as a positional record.</returns>
    public static ContractRecord? Parse(string source, string name)
    {
        var match = new Regex($"record\\s+{Regex.Escape(name)}\\s*\\(([^)]*)\\)", RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Match(source);
        if (!match.Success)
        {
            return null;
        }

        var parameters = Parameter().Matches(match.Groups[1].Value).Select(parameter => (parameter.Groups[1].Value.TrimEnd('?'), parameter.Groups[2].Value)).ToList();
        return new ContractRecord(name, parameters);
    }

    [GeneratedRegex("([A-Za-z0-9_<>?,]+)\\s+([A-Za-z0-9_]+)\\s*(?:,|$)")]
    private static partial Regex Parameter();
}
