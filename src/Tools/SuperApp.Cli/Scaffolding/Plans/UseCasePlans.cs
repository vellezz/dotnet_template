using System.Text.RegularExpressions;
using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding.Editors;

namespace SuperApp.Cli.Scaffolding.Plans;

/// <summary>Plan of <c>add usecase</c>: the files of a vertical slice and the controller action (recipes 01 and 02, ADR-0026, ADR-0032).</summary>
/// <remarks>
/// <para>
/// A command gets <c>{Name}.cs</c>, <c>{Name}Validator.cs</c> and <c>{Name}Handler.cs</c> in
/// <c>{Service}.Application/Features/{Feature}/{Name}</c>; a query gets the query, its DTO and validator there and its handler in
/// <c>{Service}.Infrastructure/Features/{Feature}</c> (ADR-0026). The action goes into <c>{Feature}Controller</c>, created when missing.
/// </para>
/// <para>
/// The scope must already be declared in <c>{Service}Scopes</c>: a new scope is registered with
/// <c>dotnet superapp add service {Service} --experience … --scope …</c> (idempotent), which also adds it to the realm and compose.
/// </para>
/// </remarks>
internal static partial class UseCasePlans
{
    /// <summary>Builds the plan of <c>add usecase</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="feature">Feature folder, usually the aggregate in plural, e.g. <c>Invoices</c>.</param>
    /// <param name="name">Use case name, a verb phrase, e.g. <c>IssueInvoice</c> or <c>GetInvoice</c>.</param>
    /// <param name="query">Whether it is a query (otherwise a command).</param>
    /// <param name="scope">Scope without the service prefix, e.g. <c>invoice.write</c>.</param>
    /// <param name="dto">Name of the result DTO of a query; derived from the name when <see langword="null"/>.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">A name is invalid, the service or scope does not exist, or the use case exists.</exception>
    public static ScaffoldPlan Add(RepositoryModel model, string serviceName, string feature, string name, bool query, string scope, string? dto)
    {
        var service = model.Services.FirstOrDefault(candidate => string.Equals(candidate.Name, serviceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ScaffoldException($"There is no service {serviceName} (dotnet superapp list services).");
        Naming.EnsurePascalCase(feature, "feature");
        Naming.EnsurePascalCase(name, "use case");
        Naming.EnsureScope(scope);
        var fullScope = $"{service.Key}.{scope}";
        if (!service.Scopes.Contains(fullScope))
        {
            throw new ScaffoldException($"{service.Name}Scopes has no scope {fullScope}; register it first: dotnet superapp add service {service.Name} --experience {service.Experience ?? "<experience>"} --scope {scope}.");
        }

        var resultDto = query ? dto ?? DefaultDto(name) : null;
        if (resultDto is not null)
        {
            Naming.EnsurePascalCase(resultDto, "DTO");
        }

        var s = service.Name;
        var slice = $"{service.Directory}/{s}.Application/Features/{feature}/{name}";
        if (model.Files.Exists($"{slice}/{name}.cs"))
        {
            throw new ScaffoldException($"{slice}/{name}.cs already exists.");
        }

        var constant = ScopesEditor.ConstantName(scope);
        var controller = $"{service.Directory}/{s}.Api/Controllers/{feature}Controller.cs";
        var hasBase = model.Files.Exists($"{service.Directory}/{s}.Api/Controllers/{s}ControllerBase.cs");
        var segment = Kebab(name);

        List<ScaffoldStep> steps;
        if (query)
        {
            steps =
            [
                new($"{slice}/{name}.cs", $"query {name} returning {resultDto}", context =>
                [
                    context.Create($"{slice}/{name}.cs", UseCaseCode.Query(s, feature, name, resultDto!, constant), "query"),
                    context.Create($"{slice}/{resultDto}.cs", UseCaseCode.Dto(s, feature, name, resultDto!), "result DTO"),
                    context.Create($"{slice}/{name}Validator.cs", UseCaseCode.Validator(s, feature, name), "validator"),
                ]),
                new($"{service.Directory}/{s}.Infrastructure/Features/{feature}/{name}Handler.cs", "query handler (Infrastructure, ADR-0026)", context =>
                    [context.Create($"{service.Directory}/{s}.Infrastructure/Features/{feature}/{name}Handler.cs", UseCaseCode.QueryHandler(s, feature, name, resultDto!), "query handler")]),
            ];
        }
        else
        {
            steps =
            [
                new($"{slice}/{name}.cs", $"command {name}", context =>
                [
                    context.Create($"{slice}/{name}.cs", UseCaseCode.Command(s, feature, name, constant), "command"),
                    context.Create($"{slice}/{name}Validator.cs", UseCaseCode.Validator(s, feature, name), "validator"),
                    context.Create($"{slice}/{name}Handler.cs", UseCaseCode.CommandHandler(s, feature, name), "command handler"),
                ]),
            ];
        }

        steps.Add(new(controller, $"action {(query ? "GET" : "POST")} {segment} in {feature}Controller", context =>
        [
            context.Create(controller, UseCaseCode.Controller(s, feature, $"v1/{Kebab(feature)}", hasBase), "controller"),
            context.Update(controller, $"action {name}", content => AddAction(content, s, feature, name, segment, fullScope, resultDto)),
        ]));

        return new ScaffoldPlan($"add usecase {s} {feature} {name} --{(query ? "query" : "command")}", steps,
        [
            query
                ? $"Write the projection in {name}Handler (Infrastructure) and the fields of {resultDto} (recipe 02); give the query its parameters (record properties) and validate them in {name}Validator."
                : $"Give {name} its parameters, write the rule in the aggregate and the orchestration in {name}Handler (recipe 01); validate the input in {name}Validator.",
            $"Adjust the action in {feature}Controller: HTTP method, route (now \"{segment}\"), parameters, success status (201 with CreatedResponse for a create) and the documented errors; replace every TODO.",
            query
                ? "Tests: the query handler on Testcontainers (integration tests), the validator (Application tests)."
                : "Tests: the aggregate rule (domain tests, no mocks), the handler with fakes (Application tests), the flow on Testcontainers.",
            $"Build (regenerates {service.Directory}/{s}.Api/openapi/{s}.Api.json), review the contract diff, then expose it to the module: dotnet superapp contracts and an action in the BFF of {service.Experience ?? "the experience"} (recipe 01, step BFF).",
            "Run dotnet test and dotnet superapp doctor.",
        ]);
    }

    /// <summary>Builds the plan of <c>remove usecase</c>: the files of the slice and its controller action.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="feature">Feature folder.</param>
    /// <param name="name">Use case name.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The use case does not exist, other code uses it, or its action cannot be found unambiguously.</exception>
    public static ScaffoldPlan Remove(RepositoryModel model, string serviceName, string feature, string name)
    {
        var service = model.Services.FirstOrDefault(candidate => string.Equals(candidate.Name, serviceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ScaffoldException($"There is no service {serviceName} (dotnet superapp list services).");
        var s = service.Name;
        var slice = $"{service.Directory}/{s}.Application/Features/{feature}/{name}";
        if (!model.Files.Exists($"{slice}/{name}.cs"))
        {
            throw new ScaffoldException($"{slice}/{name}.cs does not exist.");
        }

        var handler = $"{service.Directory}/{s}.Infrastructure/Features/{feature}/{name}Handler.cs";
        var sliceFiles = model.Files.Files(slice, "*.cs");
        var controllers = model.Files.Files($"{service.Directory}/{s}.Api/Controllers", "*.cs")
            .Where(file => model.Files.ReadOrEmpty(file).Contains($"new {name}(", StringComparison.Ordinal)).ToList();
        if (controllers.Count > 1)
        {
            throw new ScaffoldException($"{name} is sent by more than one controller ({string.Join(", ", controllers)}); remove the actions by hand.");
        }

        var dtoTypes = sliceFiles.Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList();
        UsageGuard.EnsureUnused(model.Files, [service.Directory], dtoTypes, [.. sliceFiles, handler, .. controllers], $"Use case {name}");
        List<ScaffoldStep> steps = [];
        if (controllers.Count == 1)
        {
            var controller = controllers[0];
            steps.Add(new(controller, $"action sending {name}; the controller goes with its last action", context =>
            {
                var result = context.Update(controller, $"action {name}", content => RemoveAction(content, s, feature, name));
                return context.Files.ReadOrEmpty(controller).Contains("[Http", StringComparison.Ordinal)
                    ? [result]
                    : [result, context.Delete(controller, "controller without actions")];
            }));
        }

        steps.Add(new(slice, "the slice and its handler", context => [context.Delete(slice, "slice"), context.Delete(handler, "query handler")]));
        return new ScaffoldPlan($"remove usecase {s} {feature} {name}", steps,
        [
            $"The operation disappears from {s}.Api.json at the next build: a breaking change for its consumers (the BFF, then the module); remove the BFF action and regenerate the client (dotnet superapp contracts).",
            "Remove its tests, then run dotnet build, dotnet test and dotnet superapp doctor.",
        ]);
    }

    // Removes the action that sends the use case: its documentation, attributes, signature and body (expression or block).
    private static string RemoveAction(string content, string service, string feature, string name)
    {
        var lines = TextEdits.Lines(content);
        var send = lines.FindIndex(line => line.Contains($"new {name}(", StringComparison.Ordinal));
        if (send < 0)
        {
            return content;
        }

        var signature = send;
        while (signature >= 0 && !(lines[signature].TrimStart().StartsWith("public ", StringComparison.Ordinal) && lines[signature].Contains('(', StringComparison.Ordinal)))
        {
            signature--;
        }

        if (signature < 0)
        {
            throw new ScaffoldException($"Cannot find the action sending {name} unambiguously; remove it by hand.");
        }

        // Expression body: up to the first line ending with ");". Block body: up to the closing brace at the indentation of the signature.
        var indent = lines[signature][..(lines[signature].Length - lines[signature].TrimStart().Length)];
        var end = lines[signature].TrimEnd().EndsWith("=>", StringComparison.Ordinal)
            ? lines.FindIndex(send, line => line.TrimEnd().EndsWith(");", StringComparison.Ordinal))
            : lines.FindIndex(send, line => line == indent + "}");

        if (end < 0)
        {
            throw new ScaffoldException($"Cannot find the action sending {name} unambiguously; remove it by hand.");
        }

        var start = signature;
        while (start > 0 && (lines[start - 1].TrimStart().StartsWith("///", StringComparison.Ordinal) || lines[start - 1].TrimStart().StartsWith('[')))
        {
            start--;
        }

        if (start > 0 && lines[start - 1].Length == 0)
        {
            start--;
        }
        else if (end + 1 < lines.Count && lines[end + 1].Length == 0)
        {
            end++;
        }

        lines.RemoveRange(start, end - start + 1);
        lines.Remove($"using {service}.Application.Features.{feature}.{name};");
        return TextEdits.Join(lines, content);
    }

    private static string AddAction(string content, string service, string feature, string name, string segment, string scope, string? dto)
    {
        if (content.Contains($"new {name}()", StringComparison.Ordinal) || content.Contains($"> {name}(", StringComparison.Ordinal))
        {
            return content;
        }

        if (!content.Contains("ISender sender", StringComparison.Ordinal))
        {
            throw new ScaffoldException($"{feature}Controller does not take ISender sender in its constructor; add the action by hand.");
        }

        var directive = $"using {service}.Application.Features.{feature}.{name};";
        if (!TextEdits.HasLine(content, line => line == directive))
        {
            content = TextEdits.InsertAfterLast(content, TextEdits.IsUsingDirective, [directive], "the using directives of the controller");
        }

        var lines = TextEdits.Lines(content);
        var close = lines.FindLastIndex(line => line == "}");
        if (close < 0)
        {
            throw new ScaffoldException($"Cannot find the end of {feature}Controller; add the action by hand.");
        }

        var action = UseCaseCode.Action(name, segment, scope, dto).ToList();
        if (lines[close - 1].TrimEnd() == "{")
        {
            action.RemoveAt(0);
        }

        lines.InsertRange(close, action);
        return TextEdits.Join(lines, content);
    }

    private static string DefaultDto(string name) =>
        name.StartsWith("Get", StringComparison.Ordinal) && name.Length > 3 ? $"{name[3..]}Dto" : $"{name}Dto";

    private static string Kebab(string name) => UpperCase().Replace(name, match => (match.Index > 0 ? "-" : string.Empty) + match.Value.ToLowerInvariant());

    [GeneratedRegex("[A-Z]")]
    private static partial Regex UpperCase();
}
