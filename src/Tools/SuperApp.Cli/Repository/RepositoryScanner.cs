using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SuperApp.Cli.Repository;

/// <summary>Builds the <see cref="RepositoryModel"/> by reading the files of the repository by its conventions.</summary>
/// <remarks>
/// <para>Conventions the scanner relies on (they are the ones the templates and recipes create):</para>
/// <list type="bullet">
///   <item><description>a service is a directory <c>src/Services/{Name}</c> with <c>{Name}.Domain</c>; a BFF is <c>src/Bff/{Name}.Bff</c>;</description></item>
///   <item><description>ports come from <c>Properties/launchSettings.json</c> (profile name = compose service name) and from the compose file;</description></item>
///   <item><description>scopes are <c>const string</c> values in <c>{Name}Scopes.cs</c> / <c>{Name}BffScopes.cs</c>;</description></item>
///   <item><description>event IDs are the first argument (or <c>EventId =</c>) of <c>[LoggerMessage]</c> in <c>src</c>.</description></item>
/// </list>
/// <para>A missing file never throws: the corresponding value is empty, and <c>doctor</c> reports what is missing.</para>
/// </remarks>
internal static partial class RepositoryScanner
{
    /// <summary>Reads the repository rooted at <paramref name="root"/>.</summary>
    /// <param name="root">Full path of the repository root (see <see cref="RepositoryRoot.Find"/>).</param>
    /// <returns>The model of the repository.</returns>
    public static RepositoryModel Scan(string root)
    {
        var files = new RepositoryFiles(root);
        var ranges = EventIdRegister.Read(files.ReadOrEmpty(EventIdRegister.Path));
        var compose = ComposeFile.Read(files.ReadOrEmpty(ComposeFile.Path));
        var launchPorts = LaunchPorts(files);

        var bffs = files.Directories("src/Bff")
            .Where(directory => directory.EndsWith(".Bff", StringComparison.Ordinal))
            .Select(directory => ReadBff(files, directory, launchPorts, ranges))
            .ToList();

        var services = files.Directories("src/Services")
            .Where(name => files.Directories($"src/Services/{name}").Contains($"{name}.Domain"))
            .Select(name => ReadService(files, name, launchPorts, ranges, bffs))
            .ToList();

        var ports = launchPorts
            .Concat(compose.SelectMany(service => service.HostPorts.Select(port => new PortUsage(port, service.Name, ComposeFile.Path))))
            .ToList();

        return new RepositoryModel(files, services, bffs, ports, ranges, EventIds(files), compose);
    }

    private static ServiceInfo ReadService(
        RepositoryFiles files, string name, IReadOnlyList<PortUsage> launchPorts, IReadOnlyList<EventIdRange> ranges, IReadOnlyList<BffInfo> bffs)
    {
        var directory = $"src/Services/{name}";
        var key = name.ToLowerInvariant();
        var experience = ExperienceValue().Match(files.ReadOrEmpty($"deploy/helm/superapp-service/values-{key}.yaml"));

        return new ServiceInfo(
            name,
            directory,
            experience.Success ? experience.Groups[1].Value : null,
            PortOf(launchPorts, $"{key}-api"),
            PortOf(launchPorts, $"{key}-worker"),
            Scopes(files, $"{directory}/{name}.Application", $"{name}Scopes.cs"),
            [.. ranges.Where(range => range.BelongsTo($"{name}.Api"))],
            [.. files.Files($"{directory}/{name}.Contracts", "*.cs").Select(Path.GetFileNameWithoutExtension).OfType<string>()],
            [.. files.Files($"{directory}/{name}.Worker/Consumers", "*.cs").Select(Path.GetFileNameWithoutExtension).OfType<string>()],
            [.. bffs.Where(bff => bff.Clients.Contains(name, StringComparer.Ordinal)).Select(bff => bff.Key)]);
    }

    private static BffInfo ReadBff(RepositoryFiles files, string projectDirectory, IReadOnlyList<PortUsage> launchPorts, IReadOnlyList<EventIdRange> ranges)
    {
        var name = projectDirectory[..^".Bff".Length];
        var directory = $"src/Bff/{projectDirectory}";
        return new BffInfo(
            name,
            directory,
            PortOf(launchPorts, $"{name.ToLowerInvariant()}-bff"),
            Scopes(files, directory, $"{name}BffScopes.cs"),
            files.Directories($"{directory}/Clients"),
            [.. ranges.Where(range => range.BelongsTo(projectDirectory))]);
    }

    private static int? PortOf(IReadOnlyList<PortUsage> ports, string owner) =>
        ports.FirstOrDefault(port => port.Owner == owner)?.Port;

    private static IReadOnlyList<string> Scopes(RepositoryFiles files, string directory, string fileName)
    {
        var file = files.Files(directory, fileName).FirstOrDefault();
        if (file is null)
        {
            return [];
        }

        // Constants are either full literals ("knowledge.catalog.read") or Prefix + "resource.action" (the form of the service template).
        var content = files.ReadOrEmpty(file);
        var prefix = PrefixConstant().Match(content) is { Success: true } match ? match.Groups[1].Value : string.Empty;
        return [.. ScopeConstant().Matches(content)
            .Select(scope => (scope.Groups[1].Success ? prefix : string.Empty) + scope.Groups[2].Value)
            .Where(value => value.Contains('.', StringComparison.Ordinal) && !value.EndsWith('.'))];
    }

    private static List<PortUsage> LaunchPorts(RepositoryFiles files)
    {
        var ports = new List<PortUsage>();
        foreach (var file in files.Files("src", "launchSettings.json"))
        {
            using var document = JsonDocument.Parse(files.ReadOrEmpty(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!document.RootElement.TryGetProperty("profiles", out var profiles))
            {
                continue;
            }

            foreach (var profile in profiles.EnumerateObject())
            {
                if (!profile.Value.TryGetProperty("applicationUrl", out var urls) || urls.GetString() is not { } text)
                {
                    continue;
                }

                foreach (var url in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        ports.Add(new PortUsage(uri.Port, profile.Name, file));
                    }
                }
            }
        }

        return ports;
    }

    private static List<EventIdUsage> EventIds(RepositoryFiles files)
    {
        var usages = new List<EventIdUsage>();
        var projects = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files.Files("src", "*.cs"))
        {
            var content = files.ReadOrEmpty(file);
            foreach (Match match in LoggerMessage().Matches(content))
            {
                var line = content.AsSpan(0, match.Index).Count('\n') + 1;
                usages.Add(new EventIdUsage(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), ProjectOf(files, file, projects), file, line));
            }
        }

        return usages;
    }

    private static string ProjectOf(RepositoryFiles files, string file, Dictionary<string, string> cache)
    {
        var directory = Path.GetDirectoryName(files.FullPath(file));
        while (directory is not null)
        {
            if (cache.TryGetValue(directory, out var known))
            {
                return known;
            }

            var project = Directory.GetFiles(directory, "*.csproj").FirstOrDefault();
            if (project is not null)
            {
                return cache[directory] = Path.GetFileNameWithoutExtension(project);
            }

            directory = Path.GetDirectoryName(directory);
        }

        return "?";
    }

    [GeneratedRegex("^experience:\\s*([a-z0-9-]+)\\s*$", RegexOptions.Multiline)]
    private static partial Regex ExperienceValue();

    [GeneratedRegex("const string \\w+ = (Prefix \\+ )?\"([a-z0-9][a-z0-9._]*)\";")]
    private static partial Regex ScopeConstant();

    [GeneratedRegex("const string Prefix = \"([a-z0-9]+\\.)\";")]
    private static partial Regex PrefixConstant();

    [GeneratedRegex("\\[LoggerMessage\\(\\s*(?:EventId\\s*=\\s*)?(\\d+)")]
    private static partial Regex LoggerMessage();
}
