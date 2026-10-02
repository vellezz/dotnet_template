using System.Text.RegularExpressions;
using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding.Editors;

namespace SuperApp.Cli.Scaffolding.Plans;

/// <summary>Plans of <c>add|remove scope</c>, <c>add|remove flag</c> and <c>add|remove product-event</c>.</summary>
/// <remarks>
/// <para>
/// <c>scope</c>: the constant in <c>{Service}Scopes</c>, the client scope with the service audience in the local realm and the scope requested by
/// <c>bff-web</c> (ADR-0012); the same list goes to the CIAM team (ADR-0016).
/// </para>
/// <para><c>flag</c>: a <c>FeatureFlag</c> constant in <c>{Service}FeatureFlags</c>, the class created with the first flag (ADR-0036, recipe 10).</para>
/// <para>
/// <c>product-event</c>: a forwarder consumer that maps an integration event to a product event, its name in <c>ProductEventNames</c> and the
/// forwarder's reference to the publisher's <c>Contracts</c> (ADR-0036). Properties are identifiers only, reviewed for privacy.
/// </para>
/// </remarks>
internal static partial class ElementPlans
{
    private const string Forwarder = "src/Analytics/SuperApp.AnalyticsForwarder";
    private const string EventNames = Forwarder + "/Events/ProductEventNames.cs";

    /// <summary>Builds the plan of <c>add scope</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="scope">Scope without the service prefix, e.g. <c>invoice.read</c>.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The service does not exist or the scope is invalid.</exception>
    public static ScaffoldPlan AddScope(RepositoryModel model, string serviceName, string scope)
    {
        var service = Service(model, serviceName);
        Naming.EnsureScope(scope);
        var full = $"{service.Key}.{scope}";
        var scopes = ScopesFile(service);
        return new ScaffoldPlan($"add scope {service.Name} {scope}",
        [
            new(scopes, $"constant {ScopesEditor.ConstantName(scope)}", context => [context.Update(scopes, full, content => ScopesEditor.AddConstants(content, service.Key, [scope]))]),
            new(RealmEditor.Path, $"client scope {full} with audience {service.Key}-api", context => [context.Update(RealmEditor.Path, full, content => RealmEditor.AddService(content, service.Key, [full]))]),
            new(ComposeFile.Path, $"{full} requested by bff-web", context =>
                [context.Update(ComposeFile.Path, "bff-web scopes", content => ComposeEditor.EditBffWebScopes(content, current => current.Contains(full) ? current : [.. current, full]))]),
        ],
        [
            $"Describe {full} in {service.Name}Scopes (TODO in its summary) and hand it over to the CIAM team (ADR-0016).",
            $"Use it: [RequiresScope({service.Name}Scopes.{ScopesEditor.ConstantName(scope)})] on the commands and queries it protects.",
            "Run dotnet build and dotnet superapp doctor.",
        ]);
    }

    /// <summary>Builds the plan of <c>remove scope</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="scope">Scope without the service prefix.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The service does not exist or commands and queries still require the scope.</exception>
    public static ScaffoldPlan RemoveScope(RepositoryModel model, string serviceName, string scope)
    {
        var service = Service(model, serviceName);
        var full = $"{service.Key}.{scope}";
        var scopes = ScopesFile(service);
        var constant = ScopesEditor.ConstantName(scope);
        UsageGuard.EnsureUnused(model.Files, [service.Directory], [$"{service.Name}Scopes.{constant}"], [scopes], $"Scope {full}");
        return new ScaffoldPlan($"remove scope {service.Name} {scope}",
        [
            new(scopes, $"constant {constant}", context =>
                [context.Update(scopes, full, content => ScopesEditor.RemoveConstant(ScopesEditor.RemoveConstant(content, scope), full))]),
            new(RealmEditor.Path, $"client scope {full}", context => [context.Update(RealmEditor.Path, full, content => RealmEditor.RemoveScope(content, full))]),
            new(ComposeFile.Path, $"{full} from bff-web", context =>
                [context.Update(ComposeFile.Path, "bff-web scopes", content => ComposeEditor.EditBffWebScopes(content, current => [.. current.Where(existing => existing != full)]))]),
        ],
        [$"Ask the CIAM team to remove {full} (ADR-0016); tokens already issued keep it until they expire. Run dotnet build and dotnet superapp doctor."]);
    }

    /// <summary>Builds the plan of <c>add flag</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="name">Flag name in snake_case without the service prefix, e.g. <c>invoice_reminders</c>.</param>
    /// <param name="defaultValue">Value when PostHog cannot answer: <see langword="false"/> for a new feature, <see langword="true"/> for a kill switch.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The service does not exist or the name is invalid.</exception>
    public static ScaffoldPlan AddFlag(RepositoryModel model, string serviceName, string name, bool defaultValue)
    {
        var service = Service(model, serviceName);
        if (!FlagName().IsMatch(name))
        {
            throw new ScaffoldException($"A flag name is snake_case without the service prefix (e.g. invoice_reminders); got \"{name}\".");
        }

        var key = $"{service.Key}_{name}";
        var file = FlagsFile(service);
        return new ScaffoldPlan($"add flag {service.Name} {name}",
        [
            new(file, $"FeatureFlag {ElementCode.Pascal(key)} = \"{key}\" (default {defaultValue.ToString().ToLowerInvariant()})", context =>
            [
                context.Create(file, ElementCode.FlagsClass(service.Name, service.Key), "feature flags class"),
                context.Update(file, key, content => ElementCode.AddFlag(content, key, defaultValue)),
            ]),
        ],
        [
            $"Describe the flag (TODO in its summary), create {key} in PostHog for every environment and read it in a handler: await flags.IsEnabledAsync({service.Name}FeatureFlags.{ElementCode.Pascal(key)}, cancellationToken) (recipe 10).",
            $"Locally without PostHog: FeatureFlags__{key}=true in docker-compose.yml or appsettings.Development.json.",
            "Test both values with the fake of IFeatureFlags; run dotnet test and dotnet superapp doctor.",
        ]);
    }

    /// <summary>Builds the plan of <c>remove flag</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="name">Flag name without the service prefix.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The flag does not exist or code still reads it.</exception>
    public static ScaffoldPlan RemoveFlag(RepositoryModel model, string serviceName, string name)
    {
        var service = Service(model, serviceName);
        var key = $"{service.Key}_{name}";
        var file = FlagsFile(service);
        if (!model.Files.ReadOrEmpty(file).Contains($"\"{key}\"", StringComparison.Ordinal))
        {
            throw new ScaffoldException($"{service.Name}FeatureFlags has no flag {key}.");
        }

        UsageGuard.EnsureUnused(model.Files, [service.Directory], [$"{service.Name}FeatureFlags.{ElementCode.Pascal(key)}"], [file], $"Flag {key}");
        return new ScaffoldPlan($"remove flag {service.Name} {name}",
        [
            new(file, $"flag {key}; the class goes with its last flag", context =>
            {
                var result = context.Update(file, key, content => ElementCode.RemoveFlag(content, key));
                return context.Files.ReadOrEmpty(file).Contains("new FeatureFlag(", StringComparison.Ordinal)
                    ? [result]
                    : [result, context.Delete(file, "feature flags class without flags")];
            }),
        ],
        [$"Delete {key} in PostHog and its FeatureFlags__{key} settings. Run dotnet build and dotnet superapp doctor."]);
    }

    /// <summary>Builds the plan of <c>add product-event</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="contract">Integration event type, e.g. <c>InvoiceIssuedV1</c>.</param>
    /// <param name="name">Product event name in snake_case; <c>{service}_{event}</c> when <see langword="null"/>.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The contract does not exist or is not a positional record, or the mapping exists.</exception>
    public static ScaffoldPlan AddProductEvent(RepositoryModel model, string contract, string? name)
    {
        var publisher = DomainPlans.Publisher(model, contract);
        var source = model.Files.ReadOrEmpty($"{publisher.Directory}/{publisher.Name}.Contracts/{contract}.cs");
        var record = ContractRecord.Parse(source, contract) ?? throw new ScaffoldException($"{contract} is not a positional record; map it by hand (recipe 10).");
        var consumer = DomainPlans.ConsumerName(contract);
        var file = $"{Forwarder}/Consumers/{consumer}.cs";
        if (model.Files.Exists(file))
        {
            throw new ScaffoldException($"{file} already exists.");
        }

        var baseName = consumer[..^"Consumer".Length];
        var eventName = name ?? $"{publisher.Key}_{ElementCode.Snake(baseName)}";
        var constant = publisher.Name + baseName;
        var project = $"{Forwarder}/SuperApp.AnalyticsForwarder.csproj";
        return new ScaffoldPlan($"add product-event {contract}",
        [
            new(project, $"reference {publisher.Name}.Contracts", context =>
                [context.Update(project, $"{publisher.Name}.Contracts", content => ProjectFileEditor.AddReference(content, $"..\\..\\Services\\{publisher.Name}\\{publisher.Name}.Contracts\\{publisher.Name}.Contracts.csproj"))]),
            new(EventNames, $"ProductEventNames.{constant} = \"{eventName}\"", context =>
                [context.Update(EventNames, eventName, content => ElementCode.AddEventName(content, constant, eventName, contract))]),
            new(file, $"consumer mapping {contract} to {eventName}", context =>
                [context.Create(file, ElementCode.ProductEventConsumer(publisher.Name, record, consumer, constant), "forwarder consumer")]),
        ],
        [
            $"Review the properties of {consumer} with the privacy rules (chapter 21): identifiers and categories only, never personal or health data.",
            $"Add a case to ConsumerMappingTests (name, user, time, properties) and describe {eventName} in chapter 21; run dotnet test and dotnet superapp doctor.",
        ]);
    }

    /// <summary>Builds the plan of <c>remove product-event</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="contract">Integration event type.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The mapping does not exist or tests still use it.</exception>
    public static ScaffoldPlan RemoveProductEvent(RepositoryModel model, string contract)
    {
        var publisher = DomainPlans.Publisher(model, contract);
        var consumer = DomainPlans.ConsumerName(contract);
        var file = $"{Forwarder}/Consumers/{consumer}.cs";
        if (!model.Files.Exists(file))
        {
            throw new ScaffoldException($"{file} does not exist.");
        }

        var constant = publisher.Name + consumer[..^"Consumer".Length];
        var eventName = Regex.Match(model.Files.ReadOrEmpty(EventNames), $"const string {constant} = \"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
        UsageGuard.EnsureUnused(model.Files, [Forwarder, "src/Analytics/SuperApp.AnalyticsForwarder.Tests"], [consumer, $"ProductEventNames.{constant}"], [file], $"Product event of {contract}");
        var project = $"{Forwarder}/SuperApp.AnalyticsForwarder.csproj";
        var otherUsers = model.Files.Files($"{Forwarder}/Consumers", "*.cs").Any(path => path != file && model.Files.ReadOrEmpty(path).Contains($"using {publisher.Name}.Contracts;", StringComparison.Ordinal));
        List<ScaffoldStep> steps =
        [
            new(file, "forwarder consumer", context => [context.Delete(file, "forwarder consumer")]),
            new(EventNames, $"ProductEventNames.{constant}", context => [context.Update(EventNames, eventName, content => ElementCode.RemoveEventName(content, eventName))]),
        ];
        if (!otherUsers)
        {
            steps.Add(new(project, $"reference to {publisher.Name}.Contracts", context =>
                [context.Update(project, $"{publisher.Name}.Contracts", content => ProjectFileEditor.RemoveReference(content, $"{publisher.Name}.Contracts.csproj"))]));
        }

        return new ScaffoldPlan($"remove product-event {contract}", steps,
            ["Dashboards and insights in PostHog that use the event stop receiving data. Run dotnet build, dotnet test and dotnet superapp doctor."]);
    }

    private static ServiceInfo Service(RepositoryModel model, string name) =>
        model.Services.FirstOrDefault(service => string.Equals(service.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ScaffoldException($"There is no service {name} (dotnet superapp list services).");

    private static string ScopesFile(ServiceInfo service) => $"{service.Directory}/{service.Name}.Application/{service.Name}Scopes.cs";

    private static string FlagsFile(ServiceInfo service) => $"{service.Directory}/{service.Name}.Application/{service.Name}FeatureFlags.cs";

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex FlagName();
}
