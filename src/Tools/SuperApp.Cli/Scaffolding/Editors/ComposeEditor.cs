using System.Text.RegularExpressions;

namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Adds and removes services of the local <c>docker-compose.yml</c> (ADR-0034) and edits their environment and dependencies.</summary>
/// <remarks>
/// <para>
/// Blocks are written in the layout of the existing services: domain services before the first BFF, BFFs before <c>bff-web</c>, each with
/// the network alias of its Kubernetes Service, so addresses such as <c>http://billing-api.billing.svc.cluster.local:8080</c> work the
/// same locally and in the cluster. A block ends at the next line indented by two spaces (the next service or its comment).
/// </para>
/// <para>The local passwords in connection strings are the development defaults of the compose file, never real secrets.</para>
/// </remarks>
internal static partial class ComposeEditor
{
    /// <summary>Adds the Api and Worker of a domain service before the first BFF.</summary>
    /// <param name="content">Content of the compose file.</param>
    /// <param name="service">Service name, e.g. <c>Billing</c>.</param>
    /// <param name="key">Lower-case service name.</param>
    /// <param name="apiPort">Host port of the Api.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">Neither a BFF nor <c>bff-web</c> can be found to insert before.</exception>
    public static string AddService(string content, string service, string key, int apiPort)
    {
        if (BlockRange(TextEdits.Lines(content), $"{key}-api") is not null)
        {
            return content;
        }

        var connection = $"\"Server=mssql,1433;Database=SuperApp;User Id={key}_app;Password=${{DB_APP_PASSWORD:-Dev!Passw0rd1}};TrustServerCertificate=True\"";
        string[] block =
        [
            $"  {key}-api:",
            "    profiles: [app]",
            "    build:",
            "      context: ../..",
            $"      dockerfile: src/Services/{service}/{service}.Api/Dockerfile",
            "    environment:",
            "      <<: *service-env",
            $"      ConnectionStrings__Write: {connection}",
            $"      ConnectionStrings__Read: {connection}",
            "    ports:",
            $"      - \"{apiPort}:8080\"",
            "    networks:",
            "      default:",
            $"        aliases: [{key}-api.{key}.svc.cluster.local]",
            "    depends_on: *service-deps",
            string.Empty,
            $"  {key}-worker:",
            "    profiles: [app]",
            "    build:",
            "      context: ../..",
            $"      dockerfile: src/Services/{service}/{service}.Worker/Dockerfile",
            "    environment:",
            "      <<: *service-env",
            $"      ConnectionStrings__Write: {connection}",
            $"      ConnectionStrings__Read: {connection}",
            "    depends_on: *service-deps",
            string.Empty,
        ];

        var lines = TextEdits.Lines(content);
        var before = BlockStartWithComments(lines, lines.FindIndex(line => BffKey().IsMatch(line)));
        if (before < 0)
        {
            before = BlockStartWithComments(lines, lines.FindIndex(line => line == "  bff-web:"));
        }

        if (before < 0)
        {
            throw new ScaffoldException("Cannot find where to add the service in docker-compose.yml (no BFF and no bff-web); add it by hand.");
        }

        lines.InsertRange(before, block);
        return TextEdits.Join(lines, content);
    }

    /// <summary>Adds the BFF of an experience before <c>bff-web</c>.</summary>
    /// <param name="content">Content of the compose file.</param>
    /// <param name="name">Experience name in PascalCase.</param>
    /// <param name="key">Lower-case experience name.</param>
    /// <param name="port">Host port of the BFF.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException"><c>bff-web</c> cannot be found.</exception>
    public static string AddBff(string content, string name, string key, int port)
    {
        if (BlockRange(TextEdits.Lines(content), $"{key}-bff") is not null)
        {
            return content;
        }

        string[] block =
        [
            $"  # BFF experience {name} (ADR-0038): jedyne wejście experience za bramą; woła serwisy domenowe z tokenem użytkownika (ADR-0040).",
            $"  {key}-bff:",
            "    profiles: [app]",
            "    build:",
            "      context: ../..",
            $"      dockerfile: src/Bff/{name}.Bff/Dockerfile",
            "    environment:",
            "      ASPNETCORE_ENVIRONMENT: Development",
            "      Authentication__Authority: http://keycloak:8080/realms/superapp",
            "      Authentication__RequireHttpsMetadata: \"false\"",
            "    ports:",
            $"      - \"{port}:8080\"",
            "    networks:",
            "      default:",
            $"        aliases: [{key}-bff.{key}.svc.cluster.local]",
            "    depends_on:",
            "      keycloak:",
            "        condition: service_started",
            string.Empty,
        ];

        var lines = TextEdits.Lines(content);
        var before = BlockStartWithComments(lines, lines.FindIndex(line => line == "  bff-web:"));
        if (before < 0)
        {
            throw new ScaffoldException("Cannot find bff-web in docker-compose.yml; add the BFF by hand.");
        }

        lines.InsertRange(before, block);
        return TextEdits.Join(lines, content);
    }

    /// <summary>Removes a compose service with the comment lines directly above it.</summary>
    /// <param name="content">Content of the compose file.</param>
    /// <param name="name">Compose service name, e.g. <c>billing-api</c>.</param>
    /// <returns>The new content (unchanged when the service does not exist).</returns>
    public static string RemoveService(string content, string name)
    {
        var lines = TextEdits.Lines(content);
        if (BlockRange(lines, name) is not { } range)
        {
            return content;
        }

        var (start, end) = range;
        start = BlockStartWithComments(lines, start);
        lines.RemoveRange(start, end - start);
        return TextEdits.Join(lines, content);
    }

    /// <summary>Sets an environment variable of a service (adds it after the last one; no change when it already exists).</summary>
    /// <param name="content">Content of the compose file.</param>
    /// <param name="service">Compose service name.</param>
    /// <param name="variable">Variable name, e.g. <c>Downstream__Billing__BaseAddress</c>.</param>
    /// <param name="value">Value as written in YAML.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The service or its <c>environment</c> map cannot be found.</exception>
    public static string AddEnvironment(string content, string service, string variable, string value)
    {
        var lines = TextEdits.Lines(content);
        var (start, end) = BlockRange(lines, service) ?? throw new ScaffoldException($"docker-compose.yml has no service {service}.");
        if (lines.Skip(start).Take(end - start).Any(line => line.StartsWith($"      {variable}:", StringComparison.Ordinal)))
        {
            return content;
        }

        var environment = lines.FindIndex(start, end - start, line => line == "    environment:");
        if (environment < 0)
        {
            throw new ScaffoldException($"The service {service} in docker-compose.yml has no environment map; add {variable} by hand.");
        }

        var last = environment;
        while (last + 1 < end && lines[last + 1].StartsWith("      ", StringComparison.Ordinal))
        {
            last++;
        }

        lines.Insert(last + 1, $"      {variable}: {value}");
        return TextEdits.Join(lines, content);
    }

    /// <summary>Removes an environment variable of a service.</summary>
    /// <param name="content">Content of the compose file.</param>
    /// <param name="service">Compose service name.</param>
    /// <param name="variable">Variable name.</param>
    /// <returns>The new content.</returns>
    public static string RemoveEnvironment(string content, string service, string variable)
    {
        var lines = TextEdits.Lines(content);
        if (BlockRange(lines, service) is not { } range)
        {
            return content;
        }

        var (start, end) = range;

        var index = lines.FindIndex(start, end - start, line => line.StartsWith($"      {variable}:", StringComparison.Ordinal));
        if (index >= 0)
        {
            lines.RemoveAt(index);
        }

        return TextEdits.Join(lines, content);
    }

    /// <summary>Adds a dependency (<c>condition: service_started</c>) to the explicit <c>depends_on</c> map of a service.</summary>
    /// <param name="content">Content of the compose file.</param>
    /// <param name="service">Compose service name.</param>
    /// <param name="dependency">Compose service it depends on.</param>
    /// <returns>The new content; unchanged when the service uses a shared anchor or already depends on it.</returns>
    public static string AddDependency(string content, string service, string dependency)
    {
        var lines = TextEdits.Lines(content);
        if (BlockRange(lines, service) is not { } range)
        {
            return content;
        }

        var (start, end) = range;

        var dependsOn = lines.FindIndex(start, end - start, line => line == "    depends_on:");
        if (dependsOn < 0 || lines.Skip(start).Take(end - start).Any(line => line == $"      {dependency}:"))
        {
            return content;
        }

        lines.InsertRange(dependsOn + 1, [$"      {dependency}:", "        condition: service_started"]);
        return TextEdits.Join(lines, content);
    }

    /// <summary>Removes a dependency from the <c>depends_on</c> map of a service.</summary>
    /// <param name="content">Content of the compose file.</param>
    /// <param name="service">Compose service name.</param>
    /// <param name="dependency">Compose service it depended on.</param>
    /// <returns>The new content.</returns>
    public static string RemoveDependency(string content, string service, string dependency)
    {
        var lines = TextEdits.Lines(content);
        if (BlockRange(lines, service) is not { } range)
        {
            return content;
        }

        var (start, end) = range;

        var index = lines.FindIndex(start, end - start, line => line == $"      {dependency}:");
        if (index < 0)
        {
            return content;
        }

        var count = index + 1 < end && lines[index + 1].StartsWith("        ", StringComparison.Ordinal) ? 2 : 1;
        lines.RemoveRange(index, count);
        return TextEdits.Join(lines, content);
    }

    /// <summary>Sets the scopes requested by <c>bff-web</c> (<c>Authentication__Scopes__{n}</c>), renumbering them from 0.</summary>
    /// <param name="content">Content of the compose file.</param>
    /// <param name="change">Returns the new list for the current one.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException"><c>bff-web</c> requests no scopes yet.</exception>
    public static string EditBffWebScopes(string content, Func<List<string>, List<string>> change)
    {
        var lines = TextEdits.Lines(content);
        var (start, end) = BlockRange(lines, "bff-web") ?? throw new ScaffoldException("docker-compose.yml has no bff-web service.");
        var indexes = Enumerable.Range(start, end - start).Where(index => ScopeLine().IsMatch(lines[index])).ToList();
        if (indexes.Count == 0)
        {
            throw new ScaffoldException("bff-web in docker-compose.yml requests no scopes (Authentication__Scopes__0); add them by hand.");
        }

        var scopes = indexes.Select(index => ScopeLine().Match(lines[index]).Groups[1].Value).ToList();
        var updated = change(scopes);
        if (updated.SequenceEqual(scopes))
        {
            return content;
        }

        var first = indexes[0];
        foreach (var index in indexes.OrderDescending())
        {
            lines.RemoveAt(index);
        }

        lines.InsertRange(first, updated.Select((scope, index) => $"      Authentication__Scopes__{index}: {scope}"));
        return TextEdits.Join(lines, content);
    }

    // Start and end (exclusive) of a service block: from its key line to the next line indented by two spaces or less.
    private static (int Start, int End)? BlockRange(List<string> lines, string service)
    {
        var start = lines.FindIndex(line => line == $"  {service}:");
        if (start < 0)
        {
            return null;
        }

        var end = start + 1;
        while (end < lines.Count && (lines[end].Length == 0 || lines[end].StartsWith("    ", StringComparison.Ordinal)))
        {
            end++;
        }

        return (start, end);
    }

    private static int BlockStartWithComments(List<string> lines, int keyLine)
    {
        if (keyLine < 0)
        {
            return keyLine;
        }

        var start = keyLine;
        while (start > 0 && lines[start - 1].StartsWith("  #", StringComparison.Ordinal))
        {
            start--;
        }

        return start;
    }

    [GeneratedRegex("^  [a-z0-9-]+-bff:$")]
    private static partial Regex BffKey();

    [GeneratedRegex("^      Authentication__Scopes__\\d+: (\\S+)\\s*$")]
    private static partial Regex ScopeLine();
}
