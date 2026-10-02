namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Writes the Helm values of a service or BFF and edits the <c>downstream</c> map of a BFF (ADR-0016, ADR-0041).</summary>
/// <remarks>
/// <c>experience</c> is required by the charts: it sets the <c>app.kubernetes.io/part-of</c> label that NetworkPolicy uses to keep the
/// domain services of an experience reachable only from its BFF. <c>secretName</c> names the External Secret with connection strings
/// and client secrets; it is created by the infrastructure team, never in the repository.
/// </remarks>
internal static class HelmValuesEditor
{
    /// <summary>Path of the values of a service.</summary>
    /// <param name="key">Lower-case service name.</param>
    /// <returns>The path relative to the repository root.</returns>
    public static string ServicePath(string key) => $"deploy/helm/superapp-service/values-{key}.yaml";

    /// <summary>Path of the values of a BFF.</summary>
    /// <param name="key">Lower-case experience name.</param>
    /// <returns>The path relative to the repository root.</returns>
    public static string BffPath(string key) => $"deploy/helm/superapp-bff/values-{key}.yaml";

    /// <summary>Values of a new domain service.</summary>
    /// <param name="key">Lower-case service name.</param>
    /// <param name="experience">Lower-case name of the experience the service belongs to.</param>
    /// <returns>The file content.</returns>
    public static string Service(string key, string experience) =>
        $"# Serwis domenowy experience {experience} (ADR-0038, ADR-0041). Kolejki KEDA Workera: worker.keda.queues, gdy pojawią się konsumenci.\nexperience: {experience}\nservice: {key}\nsecretName: {key}-secrets\n";

    /// <summary>Values of a new BFF.</summary>
    /// <param name="key">Lower-case experience name.</param>
    /// <returns>The file content.</returns>
    public static string Bff(string key) =>
        $"experience: {key}\nsecretName: {key}-bff-secrets\ndownstream: {{}}\n";

    /// <summary>Adds the address of a service to the <c>downstream</c> map of a BFF's values.</summary>
    /// <param name="content">Content of the values file.</param>
    /// <param name="service">Service name, the key of the map (as in <c>Downstream:{Service}:BaseAddress</c>).</param>
    /// <param name="address">Address of the service's Kubernetes Service.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The file has no <c>downstream</c> key.</exception>
    public static string AddDownstream(string content, string service, string address)
    {
        if (TextEdits.HasLine(content, line => line.StartsWith($"  {service}:", StringComparison.Ordinal)))
        {
            return content;
        }

        content = TextEdits.Lines(content).Contains("downstream: {}")
            ? TextEdits.Join(TextEdits.Lines(content).Select(line => line == "downstream: {}" ? "downstream:" : line), content)
            : content;
        var lines = TextEdits.Lines(content);
        var index = lines.IndexOf("downstream:");
        if (index < 0)
        {
            throw new ScaffoldException("The BFF values have no downstream key; add the address by hand.");
        }

        var last = index;
        while (last + 1 < lines.Count && lines[last + 1].StartsWith("  ", StringComparison.Ordinal))
        {
            last++;
        }

        lines.Insert(last + 1, $"  {service}: {address}");
        return TextEdits.Join(lines, content);
    }

    /// <summary>Removes the address of a service from the <c>downstream</c> map; an empty map becomes <c>downstream: {}</c>.</summary>
    /// <param name="content">Content of the values file.</param>
    /// <param name="service">Service name.</param>
    /// <returns>The new content.</returns>
    public static string RemoveDownstream(string content, string service)
    {
        var lines = TextEdits.Lines(content);
        lines.RemoveAll(line => line.StartsWith($"  {service}:", StringComparison.Ordinal));
        var index = lines.IndexOf("downstream:");
        if (index >= 0 && (index + 1 >= lines.Count || !lines[index + 1].StartsWith("  ", StringComparison.Ordinal)))
        {
            lines[index] = "downstream: {}";
        }

        return TextEdits.Join(lines, content);
    }
}
