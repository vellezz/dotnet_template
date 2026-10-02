namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Edits a BFF project for a client of a domain service: the Refitter settings, <c>Program.cs</c> and the <c>Downstream</c> addresses.</summary>
/// <remarks>
/// <para>
/// A client of the service <c>Billing</c> in the BFF <c>Example.Bff</c> consists of <c>Clients/Billing/billing.refitter</c> (settings of
/// the generator, pointing at the committed contract of the service), the generated <c>Clients/Billing/Generated/BillingApi.cs</c> with
/// <c>IBillingApi</c>, its registration <c>AddDownstreamApi&lt;IBillingApi&gt;(…, "Billing").AddUserTokenForwarding()</c> (the user's token
/// is forwarded unchanged, ADR-0040) and the address <c>Downstream:Billing:BaseAddress</c> (ADR-0038).
/// </para>
/// <para>Generated types are public: in the facade they are part of the BFF's own contract (ADR-0039).</para>
/// </remarks>
internal static class BffEditor
{
    private const string DownstreamNamespace = "using SuperApp.Framework.Infrastructure.Http.Downstream;";
    private const string UserContextNamespace = "using SuperApp.Framework.Infrastructure.Http.UserContext;";

    /// <summary>Settings of Refitter for a service client of a BFF, in the form used by <c>Example.Bff</c>.</summary>
    /// <param name="bff">Experience name in PascalCase, e.g. <c>Example</c>.</param>
    /// <param name="service">Service name in PascalCase.</param>
    /// <returns>The content of <c>{service}.refitter</c>.</returns>
    public static string RefitterSettings(string bff, string service) => $$"""
        {
          "openApiPath": "../../../../Services/{{service}}/{{service}}.Api/openapi/{{service}}.Api.json",
          "namespace": "{{bff}}.Bff.Clients.{{service}}",
          "naming": { "useOpenApiTitle": false, "interfaceName": "{{service}}Api" },
          "outputFolder": "./Generated",
          "outputFilename": "{{service}}Api.cs",
          "useCancellationTokens": true,
          "returnIApiResponse": true,
          "typeAccessibility": "Public",
          "usePolymorphicSerialization": true,
          "generateOperationHeaders": false,
          "operationNameTemplate": "{operationName}Async",
          "operationNameGenerator": "SingleClientFromOperationId",
          "codeGeneratorSettings": { "dateType": "System.DateOnly", "dateTimeType": "System.DateTimeOffset", "timeType": "System.TimeOnly" }
        }

        """;

    /// <summary>Registers the client in <c>Program.cs</c> (after the other clients, or before <c>builder.Build()</c>) with its usings.</summary>
    /// <param name="content">Content of <c>Program.cs</c>.</param>
    /// <param name="bff">Experience name in PascalCase.</param>
    /// <param name="service">Service name in PascalCase.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The program has neither a client registration nor <c>var app = builder.Build();</c>.</exception>
    public static string Register(string content, string bff, string service)
    {
        var registration = $"builder.Services.AddDownstreamApi<I{service}Api>(builder.Configuration, \"{service}\").AddUserTokenForwarding();";
        if (!content.Contains(registration, StringComparison.Ordinal))
        {
            content = TextEdits.HasLine(content, IsRegistration)
                ? TextEdits.InsertAfterLast(content, IsRegistration, [registration], "the client registrations")
                : TextEdits.InsertBeforeFirst(content, line => line == "var app = builder.Build();", [registration, string.Empty], "var app = builder.Build();");
        }

        foreach (var directive in (string[])[$"using {bff}.Bff.Clients.{service};", DownstreamNamespace, UserContextNamespace])
        {
            if (!TextEdits.HasLine(content, line => line == directive))
            {
                content = TextEdits.InsertAfterLast(content, TextEdits.IsUsingDirective, [directive], "the using directives");
            }
        }

        return content;
    }

    /// <summary>Removes the registration of the client and its using; the framework usings go when no client is left.</summary>
    /// <param name="content">Content of <c>Program.cs</c>.</param>
    /// <param name="bff">Experience name in PascalCase.</param>
    /// <param name="service">Service name in PascalCase.</param>
    /// <returns>The new content.</returns>
    public static string Unregister(string content, string bff, string service)
    {
        var lines = TextEdits.Lines(content);
        var index = lines.FindIndex(line => IsRegistration(line) && line.Contains($"AddDownstreamApi<I{service}Api>", StringComparison.Ordinal));
        if (index >= 0)
        {
            lines.RemoveAt(index);
            if (index > 0 && index < lines.Count && lines[index - 1].Length == 0 && lines[index].Length == 0)
            {
                lines.RemoveAt(index);
            }
        }

        lines.Remove($"using {bff}.Bff.Clients.{service};");
        if (!lines.Any(IsRegistration))
        {
            lines.Remove(DownstreamNamespace);
            lines.Remove(UserContextNamespace);
        }

        return TextEdits.Join(lines, content);
    }

    /// <summary>Adds <c>"{Service}": { "BaseAddress": "…" }</c> to the <c>Downstream</c> object of an <c>appsettings*.json</c>.</summary>
    /// <param name="content">Content of the settings file.</param>
    /// <param name="service">Service name.</param>
    /// <param name="address">Base address of the service.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">The file has no <c>"Downstream"</c> object.</exception>
    public static string AddDownstream(string content, string service, string address)
    {
        if (content.Contains($"\"{service}\": {{ \"BaseAddress\"", StringComparison.Ordinal))
        {
            return content;
        }

        var lines = TextEdits.Lines(content);
        var start = lines.FindIndex(line => line.TrimStart().StartsWith("\"Downstream\":", StringComparison.Ordinal));
        if (start < 0)
        {
            throw new ScaffoldException("The settings have no \"Downstream\" object; add the address by hand.");
        }

        var indent = lines[start][..(lines[start].Length - lines[start].TrimStart().Length)];
        var entry = $"{indent}  \"{service}\": {{ \"BaseAddress\": \"{address}\" }}";
        if (lines[start].TrimEnd().EndsWith("{}", StringComparison.Ordinal) || lines[start].TrimEnd().EndsWith("{},", StringComparison.Ordinal))
        {
            var trailing = lines[start].TrimEnd().EndsWith(',') ? "," : string.Empty;
            lines[start] = $"{indent}\"Downstream\": {{";
            lines.InsertRange(start + 1, [entry, $"{indent}}}{trailing}"]);
            return TextEdits.Join(lines, content);
        }

        var close = lines.FindIndex(start + 1, line => line.StartsWith(indent + "}", StringComparison.Ordinal));
        if (close - 1 > start && !lines[close - 1].TrimEnd().EndsWith(','))
        {
            lines[close - 1] = lines[close - 1].TrimEnd() + ",";
        }

        lines.Insert(close, entry);
        return TextEdits.Join(lines, content);
    }

    /// <summary>Removes the address of a service from the <c>Downstream</c> object; an empty object becomes <c>{}</c>.</summary>
    /// <param name="content">Content of the settings file.</param>
    /// <param name="service">Service name.</param>
    /// <returns>The new content.</returns>
    public static string RemoveDownstream(string content, string service)
    {
        var lines = TextEdits.Lines(content);
        var index = lines.FindIndex(line => line.TrimStart().StartsWith($"\"{service}\": {{ \"BaseAddress\"", StringComparison.Ordinal));
        if (index < 0)
        {
            return content;
        }

        lines.RemoveAt(index);
        var start = lines.FindLastIndex(index - 1, line => line.TrimStart().StartsWith("\"Downstream\":", StringComparison.Ordinal));
        var indent = lines[start][..(lines[start].Length - lines[start].TrimStart().Length)];
        var close = lines.FindIndex(start + 1, line => line.StartsWith(indent + "}", StringComparison.Ordinal));
        if (close == start + 1)
        {
            lines[start] = $"{indent}\"Downstream\": {{}}{(lines[close].TrimEnd().EndsWith(',') ? "," : string.Empty)}";
            lines.RemoveAt(close);
        }
        else if (lines[close - 1].TrimEnd().EndsWith(','))
        {
            lines[close - 1] = lines[close - 1].TrimEnd()[..^1];
        }

        return TextEdits.Join(lines, content);
    }

    // A registration statement, not the example in the template's comment ("//   3. builder.Services.AddDownstreamApi<I{Service}Api>...").
    private static bool IsRegistration(string line) => line.StartsWith("builder.Services.AddDownstreamApi<", StringComparison.Ordinal);
}
