using System.Text.RegularExpressions;
using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding.Editors;

namespace SuperApp.Cli.Scaffolding.Plans;

/// <summary>Plans of <c>add|remove aggregate</c>, <c>add|remove event</c> and <c>add|remove consumer</c> (recipes 03 and 05).</summary>
/// <remarks>
/// <para>
/// <c>aggregate</c>: the aggregate, its strongly typed ID, errors and repository port in Domain; the EF configuration and the repository in
/// Infrastructure, registered in <c>Add{Service}Core</c>; a first domain test. The table appears with the next migration.
/// </para>
/// <para>
/// <c>event</c>: a domain event of an aggregate; with <c>--integration</c> also the versioned contract in <c>{Service}.Contracts</c> and the
/// translator that publishes it through the outbox (ADR-0005, ADR-0027). Raising the event in the aggregate method is left to the developer.
/// </para>
/// <para>
/// <c>consumer</c>: a consumer of an integration event in the Worker, with a reference to the publisher's <c>Contracts</c> (the only project
/// of another service a service may reference, ADR-0025 rule 1).
/// </para>
/// </remarks>
internal static partial class DomainPlans
{
    /// <summary>Builds the plan of <c>add aggregate</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="feature">Domain folder, e.g. <c>Invoices</c>.</param>
    /// <param name="aggregate">Aggregate name, e.g. <c>Invoice</c>.</param>
    /// <param name="table">Table name; the plural of the aggregate when <see langword="null"/>.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">A name is invalid, the service does not exist or the aggregate exists.</exception>
    public static ScaffoldPlan AddAggregate(RepositoryModel model, string serviceName, string feature, string aggregate, string? table)
    {
        var service = Service(model, serviceName);
        Naming.EnsurePascalCase(feature, "feature");
        Naming.EnsurePascalCase(aggregate, "aggregate");
        var files = AggregateFiles(service, feature, aggregate);
        if (model.Files.Exists(files.Aggregate))
        {
            throw new ScaffoldException($"{files.Aggregate} already exists.");
        }

        var s = service.Name;
        var code = $"{service.Key}.{ElementCode.Snake(aggregate)}";
        var tableName = table ?? Plural(aggregate);
        var registration = RegistrationEditor.Path(service.Directory, s);
        return new ScaffoldPlan($"add aggregate {s} {feature} {aggregate}",
        [
            new(files.Aggregate, $"aggregate {aggregate} with {aggregate}Id, {aggregate}Errors and I{aggregate}Repository", context =>
            [
                context.Create(files.Aggregate, DomainCode.Aggregate(s, feature, aggregate), "aggregate"),
                context.Create(files.Id, DomainCode.Id(s, feature, aggregate, code), "strongly typed ID"),
                context.Create(files.Errors, DomainCode.Errors(s, feature, aggregate, code), "errors"),
                context.Create(files.RepositoryInterface, DomainCode.RepositoryInterface(s, feature, aggregate), "repository port"),
            ]),
            new(files.Configuration, $"EF configuration (table {tableName}) and repository", context =>
            [
                context.Create(files.Configuration, DomainCode.Configuration(s, feature, aggregate, tableName), "EF configuration"),
                context.Create(files.Repository, DomainCode.Repository(s, feature, aggregate), "repository"),
                context.Update(registration, $"I{aggregate}Repository", content => RegistrationEditor.AddRepository(content, s, feature, aggregate)),
            ]),
            new(files.Test, "domain test", context => [context.Create(files.Test, DomainCode.DomainTest(s, feature, aggregate), "domain test")]),
        ],
        [
            $"Model {aggregate}: state with private setters, factory and methods named in the domain language returning Result, errors in {aggregate}Errors (recipe 03).",
            $"Map the state in {aggregate}Configuration, then create the table: dotnet superapp migration add {s} Add{aggregate}.",
            $"Read side: a read model and its configuration under Persistence/Read for the queries (dotnet superapp add usecase {s} {feature} Get{aggregate} --query ...).",
            "Run dotnet build, dotnet test and dotnet superapp doctor.",
        ]);
    }

    /// <summary>Builds the plan of <c>remove aggregate</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="feature">Domain folder.</param>
    /// <param name="aggregate">Aggregate name.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The aggregate does not exist or other code (use cases, migrations) still uses it.</exception>
    public static ScaffoldPlan RemoveAggregate(RepositoryModel model, string serviceName, string feature, string aggregate)
    {
        var service = Service(model, serviceName);
        var files = AggregateFiles(service, feature, aggregate);
        if (!model.Files.Exists(files.Aggregate))
        {
            throw new ScaffoldException($"{files.Aggregate} does not exist.");
        }

        var registration = RegistrationEditor.Path(service.Directory, service.Name);
        UsageGuard.EnsureUnused(model.Files, [service.Directory], [aggregate, $"{aggregate}Id", $"{aggregate}Errors", $"I{aggregate}Repository", $"{aggregate}Repository"],
            [.. files.All, registration], $"Aggregate {aggregate}");
        var featureStillUsed = model.Files.Files($"{service.Directory}/{service.Name}.Domain/{feature}", "*.cs").Except(files.All).Any();
        return new ScaffoldPlan($"remove aggregate {service.Name} {feature} {aggregate}",
        [
            new(registration, $"registration of I{aggregate}Repository", context =>
                [context.Update(registration, $"I{aggregate}Repository", content => RegistrationEditor.RemoveRepository(content, service.Name, feature, aggregate, featureStillUsed))]),
            new(files.Aggregate, "the aggregate, its ID, errors, port, configuration, repository and test", context => [.. files.All.Select(file => context.Delete(file, "file"))]),
        ],
        ["Run dotnet build, dotnet test and dotnet superapp doctor."]);
    }

    /// <summary>Builds the plan of <c>add event</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="aggregate">Aggregate that raises the event.</param>
    /// <param name="name">Event name in the past tense, e.g. <c>InvoiceIssued</c>.</param>
    /// <param name="integration">Whether to add the integration event and its translator.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">A name is invalid, the aggregate does not exist or the event exists.</exception>
    public static ScaffoldPlan AddEvent(RepositoryModel model, string serviceName, string aggregate, string name, bool integration)
    {
        var service = Service(model, serviceName);
        Naming.EnsurePascalCase(name, "event");
        var feature = FeatureOf(model, service, aggregate);
        var files = EventFiles(service, feature, name);
        if (model.Files.Exists(files.DomainEvent))
        {
            throw new ScaffoldException($"{files.DomainEvent} already exists.");
        }

        var s = service.Name;
        List<ScaffoldStep> steps =
        [
            new(files.DomainEvent, $"domain event {name}", context => [context.Create(files.DomainEvent, DomainCode.DomainEvent(s, feature, aggregate, name), "domain event")]),
        ];
        if (integration)
        {
            steps.Add(new(files.Contract, $"integration event {name}V1 and its translator", context =>
            [
                context.Create(files.Contract, DomainCode.IntegrationEvent(s, aggregate, $"{name}V1"), "integration event"),
                context.Create(files.Translator, DomainCode.Translator(s, feature, aggregate, name, $"{name}V1"), "translator"),
            ]));
        }

        return new ScaffoldPlan($"add event {s} {aggregate} {name}{(integration ? " --integration" : string.Empty)}", steps,
        [
            $"Raise it in the method of {aggregate} that makes it happen: Raise(new {name}(Id, now)); add the data other handlers need.",
            integration
                ? $"Keep {name}V1 to the data other contexts need (primitives only); consumers: dotnet superapp add consumer <Service> --event {name}V1, analytics: dotnet superapp add product-event {name}V1."
                : "To let other contexts react, run again with --integration.",
            "Test that the method raises the event (domain test) and run dotnet test and dotnet superapp doctor.",
        ]);
    }

    /// <summary>Builds the plan of <c>remove event</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service name.</param>
    /// <param name="aggregate">Aggregate that raises the event.</param>
    /// <param name="name">Event name.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The event does not exist or code (aggregate, consumers, forwarder) still uses it.</exception>
    public static ScaffoldPlan RemoveEvent(RepositoryModel model, string serviceName, string aggregate, string name)
    {
        var service = Service(model, serviceName);
        var feature = FeatureOf(model, service, aggregate);
        var files = EventFiles(service, feature, name);
        if (!model.Files.Exists(files.DomainEvent))
        {
            throw new ScaffoldException($"{files.DomainEvent} does not exist.");
        }

        UsageGuard.EnsureUnused(model.Files, ["src"], [name, $"{name}V1", $"{name}Translator"], [files.DomainEvent, files.Contract, files.Translator], $"Event {name}");
        return new ScaffoldPlan($"remove event {service.Name} {aggregate} {name}",
        [
            new(files.DomainEvent, "domain event, integration event and translator", context =>
                [context.Delete(files.DomainEvent, "domain event"), context.Delete(files.Contract, "integration event"), context.Delete(files.Translator, "translator")]),
        ],
        ["A published integration event is a contract: make sure no consumer outside this repository depends on it. Run dotnet build, dotnet test and dotnet superapp doctor."]);
    }

    /// <summary>Builds the plan of <c>add consumer</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service whose Worker consumes the event.</param>
    /// <param name="contract">Integration event type, e.g. <c>InvoiceIssuedV1</c>.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The service or contract does not exist, or the consumer exists.</exception>
    public static ScaffoldPlan AddConsumer(RepositoryModel model, string serviceName, string contract)
    {
        var service = Service(model, serviceName);
        var publisher = Publisher(model, contract);
        var consumer = ConsumerName(contract);
        var file = $"{service.Directory}/{service.Name}.Worker/Consumers/{consumer}.cs";
        if (model.Files.Exists(file))
        {
            throw new ScaffoldException($"{file} already exists.");
        }

        var worker = $"{service.Directory}/{service.Name}.Worker/{service.Name}.Worker.csproj";
        List<ScaffoldStep> steps = [];
        if (publisher.Name != service.Name)
        {
            steps.Add(new(worker, $"reference {publisher.Name}.Contracts", context =>
                [context.Update(worker, $"ProjectReference to {publisher.Name}.Contracts", content => ProjectFileEditor.AddReference(content, ContractsReference(publisher.Name)))]));
        }

        steps.Add(new(file, $"consumer of {contract}", context => [context.Create(file, DomainCode.Consumer(service.Name, publisher.Name, contract, consumer), "consumer")]));
        return new ScaffoldPlan($"add consumer {service.Name} --event {contract}", steps,
        [
            $"Write {consumer}: inject ISender, send one command of {service.Name}.Application, log a failed Result with [LoggerMessage] (next free ID: dotnet superapp list eventids) and acknowledge it (recipe 05).",
            $"KEDA: add the queue of the consumer to worker.keda.queues in deploy/helm/superapp-service/values-{service.Key}.yaml (name: {service.Key}-{ElementCode.Snake(consumer.Replace("Consumer", string.Empty, StringComparison.Ordinal)).Replace('_', '-')}).",
            "Integration test of the consumer, then dotnet test and dotnet superapp doctor.",
        ]);
    }

    /// <summary>Builds the plan of <c>remove consumer</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="serviceName">Service whose Worker consumes the event.</param>
    /// <param name="contract">Integration event type.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The consumer does not exist or tests still use it.</exception>
    public static ScaffoldPlan RemoveConsumer(RepositoryModel model, string serviceName, string contract)
    {
        var service = Service(model, serviceName);
        var publisher = Publisher(model, contract);
        var consumer = ConsumerName(contract);
        var file = $"{service.Directory}/{service.Name}.Worker/Consumers/{consumer}.cs";
        if (!model.Files.Exists(file))
        {
            throw new ScaffoldException($"{file} does not exist.");
        }

        UsageGuard.EnsureUnused(model.Files, [service.Directory], [consumer], [file], $"Consumer {consumer}");
        var worker = $"{service.Directory}/{service.Name}.Worker/{service.Name}.Worker.csproj";
        var otherUsers = model.Files.Files($"{service.Directory}/{service.Name}.Worker", "*.cs")
            .Where(path => path != file && model.Files.ReadOrEmpty(path).Contains($"using {publisher.Name}.Contracts;", StringComparison.Ordinal));
        List<ScaffoldStep> steps = [new(file, "consumer", context => [context.Delete(file, "consumer")])];
        if (publisher.Name != service.Name && !otherUsers.Any())
        {
            steps.Add(new(worker, $"reference to {publisher.Name}.Contracts", context =>
                [context.Update(worker, $"ProjectReference to {publisher.Name}.Contracts", content => ProjectFileEditor.RemoveReference(content, $"{publisher.Name}.Contracts.csproj"))]));
        }

        return new ScaffoldPlan($"remove consumer {service.Name} --event {contract}", steps,
        [$"Remove its queue from worker.keda.queues in values-{service.Key}.yaml; the queue in RabbitMQ is deleted by the infrastructure team. Run dotnet build, dotnet test and dotnet superapp doctor."]);
    }

    /// <summary>Finds the service that publishes an integration event.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="contract">Integration event type.</param>
    /// <returns>The publishing service.</returns>
    /// <exception cref="ScaffoldException">No <c>{Service}.Contracts</c> declares the type.</exception>
    public static ServiceInfo Publisher(RepositoryModel model, string contract) =>
        model.Services.FirstOrDefault(service => service.IntegrationEvents.Contains(contract))
            ?? throw new ScaffoldException($"No {{Service}}.Contracts declares {contract}; known events: {string.Join(", ", model.Services.SelectMany(service => service.IntegrationEvents))}.");

    /// <summary>Returns the consumer class name of an integration event: <c>InvoiceIssuedV1</c> → <c>InvoiceIssuedConsumer</c>.</summary>
    /// <param name="contract">Integration event type.</param>
    /// <returns>The class name.</returns>
    public static string ConsumerName(string contract) => VersionSuffix().Replace(contract, string.Empty) + "Consumer";

    private static string ContractsReference(string publisher) => $"..\\..\\{publisher}\\{publisher}.Contracts\\{publisher}.Contracts.csproj";

    private static ServiceInfo Service(RepositoryModel model, string name) =>
        model.Services.FirstOrDefault(service => string.Equals(service.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ScaffoldException($"There is no service {name} (dotnet superapp list services).");

    // Domain folder of an existing aggregate as a namespace suffix, e.g. "Library.Favorites" for Knowledge.Domain/Library/Favorites/Favorite.cs.
    private static string FeatureOf(RepositoryModel model, ServiceInfo service, string aggregate)
    {
        var domain = $"{service.Directory}/{service.Name}.Domain";
        var file = model.Files.Files(domain, $"{aggregate}.cs").FirstOrDefault()
            ?? throw new ScaffoldException($"There is no aggregate {aggregate} in {domain}; create it with dotnet superapp add aggregate.");
        return Path.GetDirectoryName(file)![(domain.Length + 1)..].Replace('\\', '/').Replace('/', '.');
    }

    private static (string Aggregate, string Id, string Errors, string RepositoryInterface, string Configuration, string Repository, string Test, string[] All) AggregateFiles(ServiceInfo service, string feature, string aggregate)
    {
        var s = service.Name;
        var domain = $"{service.Directory}/{s}.Domain/{feature.Replace('.', '/')}";
        var write = $"{service.Directory}/{s}.Infrastructure/Persistence/Write";
        string[] all =
        [
            $"{domain}/{aggregate}.cs", $"{domain}/{aggregate}Id.cs", $"{domain}/{aggregate}Errors.cs", $"{domain}/I{aggregate}Repository.cs",
            $"{write}/Configurations/{aggregate}Configuration.cs", $"{write}/Repositories/{aggregate}Repository.cs", $"{service.Directory}/tests/{s}.Domain.Tests/{aggregate}Tests.cs",
        ];
        return (all[0], all[1], all[2], all[3], all[4], all[5], all[6], all);
    }

    private static (string DomainEvent, string Contract, string Translator) EventFiles(ServiceInfo service, string feature, string name) =>
        ($"{service.Directory}/{service.Name}.Domain/{feature.Replace('.', '/')}/Events/{name}.cs",
         $"{service.Directory}/{service.Name}.Contracts/{name}V1.cs",
         $"{service.Directory}/{service.Name}.Application/IntegrationEvents/{name}Translator.cs");

    private static string Plural(string name) =>
        name.EndsWith('y') && name.Length > 1 && !"aeiou".Contains(name[^2], StringComparison.Ordinal) ? name[..^1] + "ies"
        : name.EndsWith('s') || name.EndsWith('x') || name.EndsWith("ch", StringComparison.Ordinal) || name.EndsWith("sh", StringComparison.Ordinal) ? name + "es"
        : name + "s";

    [GeneratedRegex("V\\d+$")]
    private static partial Regex VersionSuffix();
}
