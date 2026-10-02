using System.Text.Json;

namespace SuperApp.Cli.Contracts;

/// <summary>Compares two versions of an OpenAPI 3.0 contract and classifies every difference for its consumers (ADR-0019).</summary>
/// <remarks>
/// <para>Rules (the consumer is a generated client: the module, a BFF of another experience, Refit):</para>
/// <list type="bullet">
///   <item><description><b>Breaking:</b> a removed operation or success response; a new required parameter or request property; an optional input
///   that became required; a removed response property; a changed type or format; an enum value removed from a request.</description></item>
///   <item><description><b>Warning:</b> a removed parameter or request property (clients still send it, the server ignores it); a new enum value
///   or a response property that became nullable (strict clients may fail to read it).</description></item>
///   <item><description><b>Compatible:</b> a new operation, a new optional input, a new response property.</description></item>
/// </list>
/// <para>
/// Schemas are compared through their <c>$ref</c>s, so a change of a shared schema is reported at every operation that uses it, with the
/// path of properties (<c>.items[].title</c>). Variants of polymorphic schemas (<c>anyOf</c>/<c>oneOf</c>) are matched by schema name: a new
/// variant in a response is a warning (clients must tolerate unknown discriminators), a removed one in a request is breaking. Request
/// bodies use the request rules, responses the response rules.
/// </para>
/// </remarks>
internal static class OpenApiDiff
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "patch", "head", "options"];

    /// <summary>Compares two contracts.</summary>
    /// <param name="contract">Contract file, for the report.</param>
    /// <param name="baseline">JSON of the previous version.</param>
    /// <param name="current">JSON of the current version.</param>
    /// <returns>The differences, breaking first.</returns>
    public static IReadOnlyList<ContractChange> Compare(string contract, string baseline, string current)
    {
        using var oldDocument = JsonDocument.Parse(baseline);
        using var newDocument = JsonDocument.Parse(current);
        var context = new Comparison(contract, oldDocument.RootElement, newDocument.RootElement);
        var oldPaths = Paths(oldDocument.RootElement);
        var newPaths = Paths(newDocument.RootElement);

        foreach (var ((path, method), oldOperation) in oldPaths)
        {
            var location = $"{method.ToUpperInvariant()} {path}";
            if (!newPaths.TryGetValue((path, method), out var newOperation))
            {
                context.Add(ContractChangeKind.Breaking, location, "operation removed");
                continue;
            }

            CompareParameters(context, location, oldOperation, newOperation);
            CompareRequestBody(context, location, oldOperation, newOperation);
            CompareResponses(context, location, oldOperation, newOperation);
        }

        foreach (var (path, method) in newPaths.Keys.Where(key => !oldPaths.ContainsKey(key)))
        {
            context.Add(ContractChangeKind.Compatible, $"{method.ToUpperInvariant()} {path}", "operation added");
        }

        return [.. context.Changes.OrderByDescending(change => change.Kind).ThenBy(change => change.Location, StringComparer.Ordinal)];
    }

    private static Dictionary<(string Path, string Method), JsonElement> Paths(JsonElement document)
    {
        var operations = new Dictionary<(string, string), JsonElement>();
        if (!document.TryGetProperty("paths", out var paths))
        {
            return operations;
        }

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in Methods)
            {
                if (path.Value.TryGetProperty(method, out var operation))
                {
                    operations[(path.Name, method)] = operation;
                }
            }
        }

        return operations;
    }

    private static void CompareParameters(Comparison context, string location, JsonElement oldOperation, JsonElement newOperation)
    {
        var oldParameters = Parameters(context.OldRoot, oldOperation);
        var newParameters = Parameters(context.NewRoot, newOperation);
        foreach (var (key, parameter) in newParameters)
        {
            var required = parameter.TryGetProperty("required", out var flag) && flag.GetBoolean();
            if (!oldParameters.TryGetValue(key, out var oldParameter))
            {
                context.Add(required ? ContractChangeKind.Breaking : ContractChangeKind.Compatible, $"{location} {key}", required ? "required parameter added" : "optional parameter added");
                continue;
            }

            var wasRequired = oldParameter.TryGetProperty("required", out var oldFlag) && oldFlag.GetBoolean();
            if (required && !wasRequired)
            {
                context.Add(ContractChangeKind.Breaking, $"{location} {key}", "parameter became required");
            }

            if (oldParameter.TryGetProperty("schema", out var oldSchema) && parameter.TryGetProperty("schema", out var newSchema))
            {
                context.CompareSchemas($"{location} {key}", oldSchema, newSchema, request: true, depth: 0);
            }
        }

        foreach (var key in oldParameters.Keys.Where(key => !newParameters.ContainsKey(key)))
        {
            context.Add(ContractChangeKind.Warning, $"{location} {key}", "parameter removed (clients still send it)");
        }
    }

    private static Dictionary<string, JsonElement> Parameters(JsonElement root, JsonElement operation)
    {
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (operation.TryGetProperty("parameters", out var list))
        {
            foreach (var item in list.EnumerateArray())
            {
                var parameter = Resolve(root, item);
                parameters[$"{parameter.GetProperty("in").GetString()}:{parameter.GetProperty("name").GetString()}"] = parameter;
            }
        }

        return parameters;
    }

    private static void CompareRequestBody(Comparison context, string location, JsonElement oldOperation, JsonElement newOperation)
    {
        var hadBody = oldOperation.TryGetProperty("requestBody", out var oldBody);
        var hasBody = newOperation.TryGetProperty("requestBody", out var newBody);
        if (!hasBody)
        {
            if (hadBody)
            {
                context.Add(ContractChangeKind.Warning, $"{location} body", "request body removed (clients still send it)");
            }

            return;
        }

        newBody = Resolve(context.NewRoot, newBody);
        var required = newBody.TryGetProperty("required", out var flag) && flag.GetBoolean();
        if (!hadBody)
        {
            context.Add(required ? ContractChangeKind.Breaking : ContractChangeKind.Compatible, $"{location} body", required ? "required request body added" : "optional request body added");
            return;
        }

        oldBody = Resolve(context.OldRoot, oldBody);
        if (JsonSchema(oldBody) is { } oldSchema && JsonSchema(newBody) is { } newSchema)
        {
            context.CompareSchemas($"{location} body", oldSchema, newSchema, request: true, depth: 0);
        }
    }

    private static void CompareResponses(Comparison context, string location, JsonElement oldOperation, JsonElement newOperation)
    {
        if (!oldOperation.TryGetProperty("responses", out var oldResponses) || !newOperation.TryGetProperty("responses", out var newResponses))
        {
            return;
        }

        foreach (var response in oldResponses.EnumerateObject())
        {
            if (!newResponses.TryGetProperty(response.Name, out var newResponse))
            {
                if (response.Name.StartsWith('2'))
                {
                    context.Add(ContractChangeKind.Breaking, $"{location} {response.Name}", "success response removed");
                }

                continue;
            }

            if (JsonSchema(Resolve(context.OldRoot, response.Value)) is { } oldSchema && JsonSchema(Resolve(context.NewRoot, newResponse)) is { } newSchema)
            {
                context.CompareSchemas($"{location} {response.Name}", oldSchema, newSchema, request: false, depth: 0);
            }
        }
    }

    // Schema of the JSON body (application/json or application/problem+json) of a request body or response.
    private static JsonElement? JsonSchema(JsonElement bodyOrResponse) =>
        bodyOrResponse.TryGetProperty("content", out var content)
            ? content.EnumerateObject().Where(media => media.Name.Contains("json", StringComparison.Ordinal))
                .Select(media => media.Value.TryGetProperty("schema", out var schema) ? schema : (JsonElement?)null).FirstOrDefault(schema => schema is not null)
            : null;

    private static JsonElement Resolve(JsonElement root, JsonElement element)
    {
        for (var hops = 0; hops < 16 && element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$ref", out var reference); hops++)
        {
            var current = root;
            foreach (var segment in reference.GetString()!.TrimStart('#', '/').Split('/'))
            {
                if (!current.TryGetProperty(segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal), out current))
                {
                    return element;
                }
            }

            element = current;
        }

        return element;
    }

    // State of one comparison: both documents and the collected changes, without duplicates.
    private sealed class Comparison(string contract, JsonElement oldRoot, JsonElement newRoot)
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly HashSet<string> _visited = new(StringComparer.Ordinal);

        public JsonElement OldRoot { get; } = oldRoot;

        public JsonElement NewRoot { get; } = newRoot;

        public List<ContractChange> Changes { get; } = [];

        public void Add(ContractChangeKind kind, string location, string description)
        {
            if (_seen.Add($"{location}|{description}"))
            {
                Changes.Add(new ContractChange(kind, contract, location, description));
            }
        }

        public void CompareSchemas(string location, JsonElement oldSchema, JsonElement newSchema, bool request, int depth)
        {
            if (depth == 0)
            {
                _visited.Clear();
            }

            // A $ref pair is compared once per operation input or output: stops recursive schemas (a toggle block holds blocks).
            if (depth > 32 || (Reference(oldSchema) is { } oldReference && !_visited.Add($"{oldReference}|{Reference(newSchema)}")))
            {
                return;
            }

            oldSchema = Resolve(OldRoot, oldSchema);
            newSchema = Resolve(NewRoot, newSchema);
            var oldType = Text(oldSchema, "type");
            var newType = Text(newSchema, "type");
            if (oldType is not null && newType is not null && (oldType != newType || Text(oldSchema, "format") != Text(newSchema, "format")))
            {
                Add(ContractChangeKind.Breaking, location, $"type changed from {Describe(oldSchema)} to {Describe(newSchema)}");
                return;
            }

            if (!request && !Flag(oldSchema, "nullable") && Flag(newSchema, "nullable"))
            {
                Add(ContractChangeKind.Warning, location, "response value became nullable");
            }

            CompareEnums(location, oldSchema, newSchema, request);
            CompareVariants(location, oldSchema, newSchema, request, depth);
            if (oldSchema.TryGetProperty("items", out var oldItems) && newSchema.TryGetProperty("items", out var newItems))
            {
                CompareSchemas($"{location}[]", oldItems, newItems, request, depth + 1);
            }

            var oldProperties = Properties(oldSchema);
            var newProperties = Properties(newSchema);
            var oldRequired = Required(oldSchema);
            var newRequired = Required(newSchema);
            foreach (var (name, newProperty) in newProperties)
            {
                var path = $"{location} .{name}";
                if (!oldProperties.TryGetValue(name, out var oldProperty))
                {
                    var breaking = request && newRequired.Contains(name);
                    Add(breaking ? ContractChangeKind.Breaking : ContractChangeKind.Compatible, path, breaking ? "required request property added" : "property added");
                    continue;
                }

                if (request && newRequired.Contains(name) && !oldRequired.Contains(name))
                {
                    Add(ContractChangeKind.Breaking, path, "request property became required");
                }

                CompareSchemas(path, oldProperty, newProperty, request, depth + 1);
            }

            foreach (var name in oldProperties.Keys.Where(name => !newProperties.ContainsKey(name)))
            {
                Add(request ? ContractChangeKind.Warning : ContractChangeKind.Breaking, $"{location} .{name}",
                    request ? "request property removed (clients still send it)" : "response property removed");
            }
        }

        // Polymorphic schemas (anyOf/oneOf of $refs, e.g. content blocks with a "type" discriminator): variants are matched by schema name.
        private void CompareVariants(string location, JsonElement oldSchema, JsonElement newSchema, bool request, int depth)
        {
            var oldVariants = Variants(oldSchema);
            var newVariants = Variants(newSchema);
            if (oldVariants.Count == 0 && newVariants.Count == 0)
            {
                return;
            }

            foreach (var (name, oldVariant) in oldVariants)
            {
                if (newVariants.TryGetValue(name, out var newVariant))
                {
                    CompareSchemas($"{location} <{name}>", oldVariant, newVariant, request, depth + 1);
                }
                else
                {
                    Add(request ? ContractChangeKind.Breaking : ContractChangeKind.Compatible, location, $"variant {name} removed");
                }
            }

            foreach (var name in newVariants.Keys.Where(name => !oldVariants.ContainsKey(name)))
            {
                Add(request ? ContractChangeKind.Compatible : ContractChangeKind.Warning, location, $"variant {name} added (clients must tolerate unknown variants)");
            }
        }

        private static Dictionary<string, JsonElement> Variants(JsonElement schema)
        {
            var variants = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var keyword in (string[])["anyOf", "oneOf"])
            {
                if (schema.TryGetProperty(keyword, out var list))
                {
                    var index = 0;
                    foreach (var variant in list.EnumerateArray())
                    {
                        var name = variant.TryGetProperty("$ref", out var reference) ? reference.GetString()![(reference.GetString()!.LastIndexOf('/') + 1)..] : $"{keyword}[{index}]";
                        variants[name] = variant;
                        index++;
                    }
                }
            }

            return variants;
        }

        private void CompareEnums(string location, JsonElement oldSchema, JsonElement newSchema, bool request)
        {
            var oldValues = Enum(oldSchema);
            var newValues = Enum(newSchema);
            if (oldValues.Count == 0 || newValues.Count == 0)
            {
                return;
            }

            foreach (var removed in oldValues.Except(newValues))
            {
                Add(request ? ContractChangeKind.Breaking : ContractChangeKind.Warning, location, $"enum value {removed} removed");
            }

            foreach (var added in newValues.Except(oldValues))
            {
                Add(request ? ContractChangeKind.Compatible : ContractChangeKind.Warning, location, $"enum value {added} added");
            }
        }

        private static string? Reference(JsonElement schema) =>
            schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var reference) ? reference.GetString() : null;

        private static Dictionary<string, JsonElement> Properties(JsonElement schema) =>
            schema.TryGetProperty("properties", out var properties)
                ? properties.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal)
                : [];

        private static HashSet<string> Required(JsonElement schema) =>
            schema.TryGetProperty("required", out var required) ? [.. required.EnumerateArray().Select(item => item.GetString()!)] : [];

        private static HashSet<string> Enum(JsonElement schema) =>
            schema.TryGetProperty("enum", out var values) ? [.. values.EnumerateArray().Select(value => value.ToString())] : [];

        private static string? Text(JsonElement schema, string name) =>
            schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static bool Flag(JsonElement schema, string name) =>
            schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        private static string Describe(JsonElement schema) => Text(schema, "format") is { } format ? $"{Text(schema, "type")} ({format})" : Text(schema, "type") ?? "?";
    }
}
