using System.Text.RegularExpressions;

namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Edits the code of the local edge gateway: experience policies and service scope prefixes, and the route seed (ADR-0022, ADR-0037).</summary>
/// <remarks>
/// <para>
/// <c>GatewayPolicies</c> has one constant per experience and one line <c>[Experience] = ["service.", …],</c> in <c>ServiceScopePrefixes</c>;
/// <c>ProxyConfigurationSeed</c> has one line <c>("experience", GatewayPolicies.Experience),</c> in <c>Experiences</c>. A change of the
/// seed needs a gateway migration, which the BFF plans create with <c>dotnet ef</c>.
/// </para>
/// </remarks>
internal static partial class GatewayEditor
{
    /// <summary>Path of the policies.</summary>
    public const string PoliciesPath = "src/Gateway/SuperApp.Gateway/Security/GatewayPolicies.cs";

    /// <summary>Path of the route seed.</summary>
    public const string SeedPath = "src/Gateway/SuperApp.Gateway/Persistence/Seed/ProxyConfigurationSeed.cs";

    /// <summary>Returns the name of the policy constant of an experience, e.g. <c>Example</c> for <c>example</c>.</summary>
    /// <param name="policies">Content of <c>GatewayPolicies.cs</c>.</param>
    /// <param name="experience">Lower-case experience name.</param>
    /// <returns>The constant name, or <see langword="null"/> when the experience has no policy.</returns>
    public static string? PolicyConstant(string policies, string experience) =>
        PolicyConstantPattern(experience).Match(policies) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>Adds the scope prefix of a service to the policy of its experience.</summary>
    /// <param name="content">Content of <c>GatewayPolicies.cs</c>.</param>
    /// <param name="constant">Policy constant of the experience, e.g. <c>Example</c>.</param>
    /// <param name="prefix">Scope prefix of the service, e.g. <c>billing.</c>.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The experience has no line in <c>ServiceScopePrefixes</c>.</exception>
    public static string AddScopePrefix(string content, string constant, string prefix) =>
        EditPrefixes(content, constant, prefixes => prefixes.Contains(prefix) ? prefixes : [.. prefixes, prefix]);

    /// <summary>Removes the scope prefix of a service from the policy of its experience.</summary>
    /// <param name="content">Content of <c>GatewayPolicies.cs</c>.</param>
    /// <param name="constant">Policy constant of the experience.</param>
    /// <param name="prefix">Scope prefix of the service.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The experience has no line in <c>ServiceScopePrefixes</c>.</exception>
    public static string RemoveScopePrefix(string content, string constant, string prefix) =>
        EditPrefixes(content, constant, prefixes => [.. prefixes.Where(existing => existing != prefix)]);

    /// <summary>Adds the policy of a new experience: the constant with its documentation and an empty <c>ServiceScopePrefixes</c> line.</summary>
    /// <param name="content">Content of <c>GatewayPolicies.cs</c>.</param>
    /// <param name="constant">Constant name, the PascalCase experience name.</param>
    /// <param name="experience">Lower-case experience name.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The file no longer has the expected layout.</exception>
    public static string AddExperience(string content, string constant, string experience)
    {
        if (!content.Contains($"public const string {constant} = ", StringComparison.Ordinal))
        {
            content = TextEdits.InsertBeforeFirst(content, line => line.Contains("// Scope prefixes of the domain services of each experience", StringComparison.Ordinal),
            [
                "    /// <summary>",
                $"    /// Access to the public API of the {constant} experience (its BFF, ADR-0038): an authenticated user with at least one scope of the",
                "    /// experience's domain services listed in <c>ServiceScopePrefixes</c>. The BFF and the services check the exact scopes.",
                "    /// </summary>",
                $"    public const string {constant} = \"{experience}\";",
                string.Empty,
            ], "ServiceScopePrefixes in GatewayPolicies");
        }

        if (!content.Contains($"[{constant}] = [", StringComparison.Ordinal))
        {
            content = TextEdits.InsertAfterLast(content, line => PrefixLine().IsMatch(line), [$"        [{constant}] = [],"], "the last line of ServiceScopePrefixes");
        }

        return content;
    }

    /// <summary>Removes the policy of an experience: its constant with the documentation above it and its <c>ServiceScopePrefixes</c> line.</summary>
    /// <param name="content">Content of <c>GatewayPolicies.cs</c>.</param>
    /// <param name="constant">Constant name of the experience.</param>
    /// <returns>The new content.</returns>
    public static string RemoveExperience(string content, string constant)
    {
        var lines = TextEdits.Lines(content);
        var index = lines.FindIndex(line => line.Contains($"public const string {constant} = ", StringComparison.Ordinal));
        if (index >= 0)
        {
            var start = index;
            while (start > 0 && lines[start - 1].TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                start--;
            }

            var end = index + 1 < lines.Count && lines[index + 1].Length == 0 ? index + 1 : index;
            lines.RemoveRange(start, end - start + 1);
        }

        return TextEdits.RemoveLines(TextEdits.Join(lines, content), line => line.TrimStart().StartsWith($"[{constant}] = [", StringComparison.Ordinal));
    }

    /// <summary>Adds the route of an experience to the seed.</summary>
    /// <param name="content">Content of <c>ProxyConfigurationSeed.cs</c>.</param>
    /// <param name="experience">Lower-case experience name.</param>
    /// <param name="constant">Policy constant of the experience.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The <c>Experiences</c> array cannot be found.</exception>
    public static string AddRoute(string content, string experience, string constant) =>
        content.Contains($"(\"{experience}\", GatewayPolicies.{constant}),", StringComparison.Ordinal)
            ? content
            : TextEdits.InsertAfterLast(content, line => SeedLine().IsMatch(line), [$"        (\"{experience}\", GatewayPolicies.{constant}),"], "the Experiences array of the route seed");

    /// <summary>Removes the route of an experience from the seed.</summary>
    /// <param name="content">Content of <c>ProxyConfigurationSeed.cs</c>.</param>
    /// <param name="experience">Lower-case experience name.</param>
    /// <returns>The new content.</returns>
    public static string RemoveRoute(string content, string experience) =>
        TextEdits.RemoveLines(content, line => SeedLine().IsMatch(line) && line.Contains($"(\"{experience}\",", StringComparison.Ordinal));

    private static string EditPrefixes(string content, string constant, Func<List<string>, List<string>> change)
    {
        var lines = TextEdits.Lines(content);
        var index = lines.FindIndex(line => line.TrimStart().StartsWith($"[{constant}] = [", StringComparison.Ordinal));
        if (index < 0)
        {
            throw new ScaffoldException($"{PoliciesPath} has no ServiceScopePrefixes line for {constant}; add the experience first (dotnet superapp add bff).");
        }

        var match = PrefixLine().Match(lines[index]);
        var prefixes = Quoted().Matches(match.Groups[2].Value).Select(item => item.Groups[1].Value).ToList();
        var updated = change(prefixes);
        if (updated.SequenceEqual(prefixes))
        {
            return content;
        }

        lines[index] = $"{match.Groups[1].Value}[{constant}] = [{string.Join(", ", updated.Select(prefix => $"\"{prefix}\""))}],";
        return TextEdits.Join(lines, content);
    }

    private static Regex PolicyConstantPattern(string experience) => new($"public const string (\\w+) = \"{Regex.Escape(experience)}\";", RegexOptions.None, TimeSpan.FromSeconds(1));

    [GeneratedRegex("^(\\s*)\\[\\w+\\] = \\[(.*)\\],\\s*$")]
    private static partial Regex PrefixLine();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex Quoted();

    [GeneratedRegex("^\\s*\\(\"[a-z0-9-]+\", GatewayPolicies\\.\\w+\\),\\s*$")]
    private static partial Regex SeedLine();
}
