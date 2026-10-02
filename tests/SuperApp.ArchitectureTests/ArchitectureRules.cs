using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application;
using ArchUnitNET.xUnitV3;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace SuperApp.ArchitectureTests;

/// <summary>
/// Architecture rules of the whole solution that neither project references nor <c>SuperApp.Analyzers</c> can enforce (ADR-0025).
/// Rules are numbered as in ADR-0025.
/// </summary>
/// <remarks>
/// <para>
/// The rules run against the compiled production assemblies loaded by <see cref="Solution"/>. Services are discovered by naming
/// convention (<c>{Service}.Domain</c>), so every per-service rule is a theory that runs once per service and a new service is
/// covered without changing this class; it only has to be referenced from the csproj.
/// </para>
/// <para>
/// A failing rule means the code breaks a decision recorded in an ADR: fix the code, not the test. A new rule is added only when an ADR
/// introduces a principle that the compiler and the analyzers do not already enforce. Most rules use ArchUnitNET; the reflection-based
/// ones (4, 7, 8) check what ArchUnitNET cannot express directly.
/// </para>
/// </remarks>
public sealed class ArchitectureRules
{
    /// <summary>Names of the discovered services (e.g. <c>Knowledge</c>, <c>SleepDiary</c>), the data of every per-service theory.</summary>
    public static TheoryData<string> Services => [.. Solution.Services];

    // Guard: if discovery breaks (e.g. assemblies not copied to the output), every theory would silently run zero times.
    [Fact]
    public void Services_are_discovered() => Assert.NotEmpty(Solution.Services);

    // 1. A service does not depend on another service: bounded contexts share no model and talk through events or
    //    ACL-wrapped HTTP clients generated from contracts (ADR-0002). Only SuperApp.Framework may be shared, plus the other
    //    service's Contracts (its published language), which a Worker needs to consume that service's integration events.
    [Theory]
    [MemberData(nameof(Services))]
    public void Service_does_not_depend_on_other_services(string service)
    {
        foreach (var other in Solution.Services.Where(other => other != service))
        {
            Types().That().ResideInNamespaceMatching($"^{service}(\\..*)?$")
                .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching($"^{other}(?!\\.Contracts(\\.|$))(\\..*)?$"))
                .Check(Solution.Architecture);
        }
    }

    // 2. Domain depends only on itself, SuperApp.Framework.Domain and the base class library: no EF Core, MediatR, MassTransit,
    //    ASP.NET or Refit (ADR-0002). Microsoft.CodeAnalysis is allowed for compiler-generated attributes.
    [Theory]
    [MemberData(nameof(Services))]
    public void Domain_depends_only_on_framework_domain(string service) =>
        Types().That().ResideInNamespaceMatching($"^{service}\\.Domain(\\..*)?$")
            .Should().OnlyDependOnTypesThat().ResideInNamespaceMatching($"^(System|Microsoft\\.CodeAnalysis|{service}\\.Domain|SuperApp\\.Framework\\.Domain)(\\..*)?$")
            .Check(Solution.Architecture);

    // 3. Application does not depend on Infrastructure (its own or the framework's), EF Core or MassTransit: it defines ports,
    //    Infrastructure implements them (dependency rule, ADR-0002).
    [Theory]
    [MemberData(nameof(Services))]
    public void Application_does_not_depend_on_infrastructure(string service) =>
        Types().That().ResideInNamespaceMatching($"^{service}\\.Application(\\..*)?$")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(
                $"^({service}\\.Infrastructure|SuperApp\\.Framework\\.Infrastructure|Microsoft\\.EntityFrameworkCore|MassTransit)(\\..*)?$"))
            .Check(Solution.Architecture);

    // 4. Contracts (the published language of a service, versioned separately) do not depend on Domain and expose only
    //    primitive property types, never IDs or value objects, so consumers need no domain types (ADR-0024).
    //    "Primitive" = IsPrimitive, string, Guid, decimal, date/time types and generic collections of them; see IsPrimitive.
    [Theory]
    [MemberData(nameof(Services))]
    public void Contracts_contain_only_primitive_types(string service)
    {
        Types().That().ResideInNamespaceMatching($"^{service}\\.Contracts(\\..*)?$")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching($"^{service}\\.Domain(\\..*)?$"))
            .WithoutRequiringPositiveResults() // a new service (dotnet superapp add service) has no integration events yet
            .Check(Solution.Architecture);

        var contracts = Solution.Assemblies.Single(assembly => assembly.GetName().Name == $"{service}.Contracts");
        var properties = contracts.GetExportedTypes().SelectMany(type => type.GetProperties().Select(property => (type, property)));
        Assert.All(properties, pair => Assert.True(
            IsPrimitive(pair.property.PropertyType),
            $"{pair.type.Name}.{pair.property.Name}: typ {pair.property.PropertyType.Name} nie jest prymitywem (ADR-0024)."));
    }

    // 5. FromTrusted skips validation, so it may be called only where the value is already known to be valid: Infrastructure
    //    (materializing from the database) and the Domain types themselves. Application, Api and Worker receive untrusted input
    //    and must use Create, which returns a Result (ADR-0023, ADR-0024).
    [Theory]
    [MemberData(nameof(Services))]
    public void FromTrusted_is_used_only_by_infrastructure(string service) =>
        Types().That().ResideInNamespaceMatching($"^{service}\\.(Application|Api|Worker)(\\..*)?$")
            .Should().NotCallAny(MethodMembers().That().HaveNameStartingWith("FromTrusted("))
            .Check(Solution.Architecture);

    // Guard for rule 5: ArchUnitNET matches methods by full signature, so this proves the "FromTrusted(" prefix still finds them.
    [Fact]
    public void FromTrusted_rule_matches_existing_methods() =>
        Assert.NotEmpty(MethodMembers().That().HaveNameStartingWith("FromTrusted(").GetObjects(Solution.Architecture));

    // 6. Api and Worker use the service's Infrastructure only in the composition root (DI registration in Program.cs).
    //    Program.cs uses top-level statements and lives in the global namespace, so it is outside the checked namespaces;
    //    controllers and consumers must go through ISender and Application ports (ADR-0002).
    [Theory]
    [MemberData(nameof(Services))]
    public void Hosts_use_infrastructure_only_in_composition_root(string service) =>
        Types().That().ResideInNamespaceMatching($"^{service}\\.(Api|Worker)(\\..*)?$")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching($"^{service}\\.Infrastructure(\\..*)?$"))
            .Check(Solution.Architecture);

    // 7. No assembly references SuperApp.Migrator: it is the only project referencing the Infrastructure of every service and
    //    exists solely to run migrations (ADR-0004).
    [Fact]
    public void Nothing_references_migrator() =>
        Assert.DoesNotContain(
            Solution.Assemblies.Where(assembly => assembly.GetName().Name != "SuperApp.Migrator"),
            assembly => assembly.GetReferencedAssemblies().Any(reference => reference.Name == "SuperApp.Migrator"));

    // 8. Handlers (MediatR request handlers and domain event handlers) are internal sealed. Command handlers live in Application,
    //    query handlers in Infrastructure because they use ReadDbContext directly (ADR-0026), and domain event handlers in
    //    Application (translation to integration events, ADR-0027) or Infrastructure (technical, e.g. cache invalidation, ADR-0020),
    //    never in Api or Worker. The layer is taken from the last segment of the assembly name.
    [Fact]
    public void Handlers_are_internal_sealed_and_in_the_right_layer()
    {
        var handlers = Solution.Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.GetInterfaces().Any(IsHandlerInterface))
            .ToList();

        Assert.NotEmpty(handlers);
        Assert.All(handlers, handler =>
        {
            Assert.True(handler.IsNotPublic && handler.IsSealed, $"{handler.FullName} musi być internal sealed.");

            var layer = handler.Assembly.GetName().Name!.Split('.').Last();
            if (Implements(handler, typeof(ICommandHandler<,>)))
            {
                Assert.True(layer == "Application", $"{handler.FullName}: handler komendy poza Application.");
            }

            if (Implements(handler, typeof(IQueryHandler<,>)))
            {
                Assert.True(layer == "Infrastructure", $"{handler.FullName}: handler zapytania poza Infrastructure (ADR-0026).");
            }

            if (Implements(handler, typeof(IDomainEventHandler<>)))
            {
                Assert.True(layer is "Application" or "Infrastructure", $"{handler.FullName}: handler zdarzenia domenowego w warstwie {layer}.");
            }
        });
    }

    // 9. Pipeline behaviors are registered in the order of ADR-0017: logging/telemetry -> authorization -> validation -> transaction.
    //    MediatR runs them in registration order, so authorization happens before validation and only valid commands open a transaction.
    [Fact]
    public void Pipeline_behaviors_are_registered_in_order()
    {
        var services = new ServiceCollection();
        services.AddAppApplication(typeof(ArchitectureRules).Assembly);

        var order = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType!.Name.Split('`')[0])
            .ToList();

        Assert.Equal(["LoggingBehavior", "AuthorizationBehavior", "ValidationBehavior", "TransactionBehavior"], order);
    }

    // 10. The analytics forwarder knows the services only through their Contracts (published language): it references no Domain,
    //     Application, Infrastructure, Api or Worker of any service, so it cannot reach into a service's model or database (ADR-0036).
    [Fact]
    public void Analytics_forwarder_depends_only_on_contracts_of_services()
    {
        var forwarder = Solution.Assemblies.Single(assembly => assembly.GetName().Name == "SuperApp.AnalyticsForwarder");
        var serviceReferences = forwarder.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => Solution.Services.Any(service => name.StartsWith(service + ".", StringComparison.Ordinal)))
            .ToList();

        Assert.NotEmpty(serviceReferences);
        Assert.All(serviceReferences, name => Assert.EndsWith(".Contracts", name, StringComparison.Ordinal));
    }

    // 11. Only the framework (feature flags, analytics identity) and the analytics forwarder use the PostHog SDK. Services read flags
    //     through IFeatureFlags and never capture events themselves; backend events come from integration events (ADR-0036).
    [Fact]
    public void Only_framework_and_forwarder_use_posthog()
    {
        string[] allowed = ["SuperApp.Framework.Infrastructure", "SuperApp.AnalyticsForwarder"];
        var users = Solution.Assemblies
            .Where(assembly => assembly.GetReferencedAssemblies().Any(reference => reference.Name == "PostHog"))
            .Select(assembly => assembly.GetName().Name!)
            .ToList();

        Assert.Contains("SuperApp.Framework.Infrastructure", users);
        Assert.All(users, name => Assert.Contains(name, allowed));
    }

    // 12. A BFF knows the services only through the clients generated from their contracts (Refitter, code inside the BFF): it references
    //     no assembly of any service (not even Contracts) and no other BFF (ADR-0038). It talks to other experiences only through their
    //     internal API contracts, also as generated clients.
    [Fact]
    public void Bff_references_no_service_and_no_other_bff()
    {
        Assert.NotEmpty(Solution.Bffs);
        foreach (var bff in Solution.Bffs)
        {
            var forbidden = bff.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => Solution.Services.Any(service => name.StartsWith(service + ".", StringComparison.Ordinal))
                               || (name.EndsWith(".Bff", StringComparison.Ordinal) && name != bff.GetName().Name))
                .ToList();
            Assert.Empty(forbidden);
        }
    }

    // 13. A BFF has no database: no DbContext, no migrations; it holds no state of its own (ADR-0038).
    [Fact]
    public void Bff_has_no_database()
    {
        foreach (var bff in Solution.Bffs)
        {
            Assert.DoesNotContain(bff.GetTypes(), type => typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(type));
        }
    }

    // 14. Domain services never depend on a BFF: the BFF calls them, never the other way round (ADR-0038).
    [Fact]
    public void Services_do_not_reference_bffs() =>
        Assert.DoesNotContain(
            Solution.Assemblies.Where(assembly => Solution.Services.Any(service => assembly.GetName().Name!.StartsWith(service + ".", StringComparison.Ordinal))),
            assembly => assembly.GetReferencedAssemblies().Any(reference => reference.Name!.EndsWith(".Bff", StringComparison.Ordinal)));

    // 15. Production code never references SuperApp.Framework.Testing: its ResultAssert throws instead of handling a failure, which is
    //     right in a test and a bug in a handler; production code reads results with TryGetValue and IsFailure (ADR-0047).
    [Fact]
    public void Production_code_does_not_reference_test_helpers() =>
        Assert.DoesNotContain(
            Solution.Assemblies,
            assembly => assembly.GetReferencedAssemblies().Any(reference => reference.Name == "SuperApp.Framework.Testing"));

    // Command and query handlers are MediatR IRequestHandler<,>; domain event handlers use the framework's own interface.
    private static bool IsHandlerInterface(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) || type.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>));

    private static bool Implements(Type type, Type openGeneric) =>
        type.GetInterfaces().Any(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == openGeneric);

    private static bool IsPrimitive(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type != typeof(string) && type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            return type.GenericTypeArguments.All(IsPrimitive);
        }

        return type.IsPrimitive
            || type == typeof(string) || type == typeof(Guid) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly)
            || type == typeof(TimeOnly) || type == typeof(TimeSpan);
    }
}
