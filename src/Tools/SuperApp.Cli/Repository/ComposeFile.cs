using System.Globalization;
using System.Text.RegularExpressions;

namespace SuperApp.Cli.Repository;

/// <summary>Reads the services of <c>deploy/local/docker-compose.yml</c> (ADR-0034) without a YAML library.</summary>
/// <remarks>
/// The file follows a fixed layout: services are keys indented by two spaces under the top-level <c>services:</c>, ports are
/// <c>- "host:container"</c> items and environment variables are <c>Name: value</c> entries of the <c>environment:</c> map. Anchors and
/// extension fields (<c>x-…</c>) outside <c>services:</c> are ignored. A different layout is not an error: the service is simply not found,
/// and <c>doctor</c> reports it as missing.
/// </remarks>
internal static partial class ComposeFile
{
    /// <summary>Path of the file relative to the repository root.</summary>
    public const string Path = "deploy/local/docker-compose.yml";

    /// <summary>Parses the services of the file.</summary>
    /// <param name="content">Content of the file; empty when it does not exist.</param>
    /// <returns>The services in file order.</returns>
    public static IReadOnlyList<ComposeService> Read(string content)
    {
        var services = new List<ComposeService>();
        string? topLevel = null;
        string? current = null;
        string? section = null;
        var ports = new List<int>();
        var environment = new List<string>();

        void Flush()
        {
            if (current is not null)
            {
                services.Add(new ComposeService(current, [.. ports], [.. environment]));
            }

            current = null;
            section = null;
            ports.Clear();
            environment.Clear();
        }

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            if (indent == 0)
            {
                Flush();
                topLevel = line.Split(':')[0];
                continue;
            }

            if (topLevel != "services")
            {
                continue;
            }

            if (indent == 2 && ServiceKey().Match(line) is { Success: true } service)
            {
                Flush();
                current = service.Groups[1].Value;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (indent == 4)
            {
                section = line.Trim().TrimEnd(':');
                continue;
            }

            if (section == "ports" && PortMapping().Match(line) is { Success: true } port)
            {
                ports.Add(int.Parse(port.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            else if (section == "environment" && indent == 6 && EnvironmentKey().Match(line) is { Success: true } key)
            {
                environment.Add(key.Groups[1].Value);
            }
        }

        Flush();
        return services;
    }

    [GeneratedRegex("^  ([A-Za-z0-9_.-]+):\\s*$")]
    private static partial Regex ServiceKey();

    [GeneratedRegex("^\\s*-\\s*\"?(\\d+):\\d+\"?")]
    private static partial Regex PortMapping();

    [GeneratedRegex("^\\s*([A-Za-z0-9_]+):")]
    private static partial Regex EnvironmentKey();
}
