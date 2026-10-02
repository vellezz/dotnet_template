using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding.Editors;

namespace SuperApp.Cli.Tests;

/// <summary>
/// Every editor applied to the real files of the repository: adding and then removing a component gives the original file byte for byte
/// (so <c>add</c> + <c>remove</c> leave no trace), and an addition lands in the right place.
/// </summary>
public sealed class EditorRoundTripTests
{
    private static readonly RepositoryFiles Files = new(RepositoryRoot.Find(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Tests must run inside the repository."));

    [Fact]
    public void Slnx_add_and_remove_restore_the_solution()
    {
        var original = Files.ReadOrEmpty(SolutionFiles.Slnx);
        string[] projects = ["src/Services/Billing/Billing.Api/Billing.Api.csproj", "src/Services/Billing/tests/Billing.Domain.Tests/Billing.Domain.Tests.csproj"];

        var added = SlnxEditor.Add(original, projects);

        Assert.Contains(SolutionFiles.ReadSlnx(added), entry => entry is { Folder: "src/Services/Billing/tests", ProjectPath: "src/Services/Billing/tests/Billing.Domain.Tests/Billing.Domain.Tests.csproj" });
        Assert.Equal(original, SlnxEditor.Add(original, []));
        Assert.Equal(original, SlnxEditor.Remove(added, projects));
    }

    [Fact]
    public void Realm_service_and_bff_round_trip()
    {
        var original = Files.ReadOrEmpty(RealmEditor.Path);

        var withService = RealmEditor.AddService(original, "billing", ["billing.invoice.read"]);
        var withBff = RealmEditor.AddBff(original, "coaching", "Coaching");

        Assert.Contains("\"clientId\": \"billing-client\"", withService, StringComparison.Ordinal);
        Assert.Contains("\"included.custom.audience\": \"billing-api\"", withService, StringComparison.Ordinal);
        Assert.Equal(withService, RealmEditor.AddService(withService, "billing", ["billing.invoice.read"]));
        Assert.Equal(original, RealmEditor.RemoveService(withService, "billing"));
        Assert.Equal(original, RealmEditor.RemoveBff(withBff, "coaching"));
    }

    [Fact]
    public void Compose_service_bff_environment_and_scopes_round_trip()
    {
        var original = Files.ReadOrEmpty(ComposeFile.Path);

        var added = ComposeEditor.AddService(original, "Billing", "billing", 5103);
        added = ComposeEditor.EditBffWebScopes(added, scopes => [.. scopes, "billing.invoice.read"]);
        added = ComposeEditor.AddBff(added, "Coaching", "coaching", 5121);
        added = ComposeEditor.AddDependency(ComposeEditor.AddEnvironment(added, "example-bff", "Downstream__Billing__BaseAddress", "http://billing-api.billing.svc.cluster.local:8080"), "example-bff", "billing-api");

        var services = ComposeFile.Read(added);
        Assert.Contains(services, service => service is { Name: "billing-api", HostPorts: [5103] });
        Assert.Contains("Downstream__Billing__BaseAddress", services.Single(service => service.Name == "example-bff").Environment);

        var removed = ComposeEditor.RemoveDependency(ComposeEditor.RemoveEnvironment(added, "example-bff", "Downstream__Billing__BaseAddress"), "example-bff", "billing-api");
        removed = ComposeEditor.RemoveService(removed, "coaching-bff");
        removed = ComposeEditor.EditBffWebScopes(removed, scopes => [.. scopes.Where(scope => !scope.StartsWith("billing.", StringComparison.Ordinal))]);
        removed = ComposeEditor.RemoveService(ComposeEditor.RemoveService(removed, "billing-api"), "billing-worker");
        Assert.Equal(original, removed);
    }

    [Fact]
    public void Migrator_registration_round_trip_puts_the_using_among_the_directives()
    {
        var original = Files.ReadOrEmpty(MigratorEditor.ProgramPath);

        var added = MigratorEditor.Register(original, "Billing");

        var lines = added.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        Assert.True(lines.IndexOf("using Billing.Infrastructure.Persistence.Write;") < lines.FindIndex(line => line.StartsWith("var builder", StringComparison.Ordinal)));
        Assert.Contains("typeof(SleepDiaryWriteDbContext), typeof(BillingWriteDbContext)]", added, StringComparison.Ordinal);
        Assert.Equal(added, MigratorEditor.Register(added, "Billing"));
        Assert.Equal(original, MigratorEditor.Unregister(added, "Billing"));
    }

    [Fact]
    public void Bootstrap_sql_project_references_and_helm_downstream_round_trip()
    {
        var sql = Files.ReadOrEmpty(BootstrapSqlEditor.Path);
        var tests = Files.ReadOrEmpty("tests/SuperApp.ArchitectureTests/SuperApp.ArchitectureTests.csproj");
        var values = Files.ReadOrEmpty(HelmValuesEditor.BffPath("example"));
        const string Include = "..\\..\\src\\Services\\Billing\\Billing.Api\\Billing.Api.csproj";

        Assert.Contains("(N'billing');", BootstrapSqlEditor.Add(sql, "billing"), StringComparison.Ordinal);
        Assert.Equal(sql, BootstrapSqlEditor.Remove(BootstrapSqlEditor.Add(sql, "billing"), "billing"));
        Assert.Equal(tests, ProjectFileEditor.RemoveReference(ProjectFileEditor.AddReference(tests, Include), "Billing.Api.csproj"));
        Assert.Equal(values, HelmValuesEditor.RemoveDownstream(HelmValuesEditor.AddDownstream(values, "Billing", "http://billing"), "Billing"));
        Assert.Equal(HelmValuesEditor.Bff("x"), HelmValuesEditor.RemoveDownstream(HelmValuesEditor.AddDownstream(HelmValuesEditor.Bff("x"), "Billing", "http://billing"), "Billing"));
    }

    [Fact]
    public void Gateway_experience_route_and_scope_prefix_round_trip()
    {
        var policies = Files.ReadOrEmpty(GatewayEditor.PoliciesPath);
        var seed = Files.ReadOrEmpty(GatewayEditor.SeedPath);

        var withExperience = GatewayEditor.AddScopePrefix(GatewayEditor.AddExperience(policies, "Coaching", "coaching"), "Coaching", "plans.");

        Assert.Equal("Coaching", GatewayEditor.PolicyConstant(withExperience, "coaching"));
        Assert.Contains("[Coaching] = [\"plans.\"],", withExperience, StringComparison.Ordinal);
        Assert.Contains("[Example] = [\"knowledge.\", \"sleepdiary.\", \"billing.\"],", GatewayEditor.AddScopePrefix(policies, "Example", "billing."), StringComparison.Ordinal);
        Assert.Equal(policies, GatewayEditor.RemoveExperience(GatewayEditor.RemoveScopePrefix(withExperience, "Coaching", "plans."), "Coaching"));
        Assert.Equal(seed, GatewayEditor.RemoveRoute(GatewayEditor.AddRoute(seed, "coaching", "Coaching"), "coaching"));
    }

    [Fact]
    public void Event_id_register_gives_ranges_from_the_pool_and_takes_them_back()
    {
        var original = Files.ReadOrEmpty(EventIdRegister.Path);
        var pool = EventIdRegister.Read(original).Single(range => range.Component.StartsWith("kolejne", StringComparison.Ordinal));

        var first = EventIdRegisterEditor.Allocate(original, "Billing", out var billing);
        var second = EventIdRegisterEditor.Allocate(first, "`Coaching.Bff` (BFF experience Coaching)", out var coaching);

        Assert.Equal($"{pool.Start}–{pool.Start + 999}", billing);
        Assert.Equal($"{pool.Start + 1000}–{pool.Start + 1999}", coaching);
        Assert.Equal(second, EventIdRegisterEditor.Allocate(second, "Billing", out _));
        Assert.Equal(original, EventIdRegisterEditor.Release(EventIdRegisterEditor.Release(second, "`Coaching.Bff` (BFF experience Coaching)"), "Billing"));
        Assert.Equal(original, EventIdRegisterEditor.Release(EventIdRegisterEditor.Release(second, "Billing"), "`Coaching.Bff` (BFF experience Coaching)"));
    }

    [Fact]
    public void Bff_client_registration_skips_the_template_comment_and_round_trips()
    {
        var program = Files.ReadOrEmpty($"{RepositoryFiles.TemplatesDirectory}/superapp-bff/ExperienceName.Bff/Program.cs");
        var settings = Files.ReadOrEmpty($"{RepositoryFiles.TemplatesDirectory}/superapp-bff/ExperienceName.Bff/appsettings.json");
        var example = Files.ReadOrEmpty("src/Bff/Example.Bff/appsettings.Development.json");

        var registered = BffEditor.Register(program, "ExperienceName", "Plans");

        var lines = registered.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var registration = lines.FindIndex(line => line.StartsWith("builder.Services.AddDownstreamApi<IPlansApi>", StringComparison.Ordinal));
        Assert.True(registration > lines.FindIndex(line => line.StartsWith("var builder", StringComparison.Ordinal)));
        Assert.True(registration < lines.IndexOf("var app = builder.Build();"));
        Assert.Equal(program, BffEditor.Unregister(registered, "ExperienceName", "Plans"));
        Assert.Equal(settings, BffEditor.RemoveDownstream(BffEditor.AddDownstream(settings, "Plans", "http://plans"), "Plans"));
        Assert.Equal(example, BffEditor.RemoveDownstream(BffEditor.AddDownstream(example, "Plans", "http://localhost:5103"), "Plans"));
    }

    [Fact]
    public void Scope_constants_use_the_prefix_of_the_template()
    {
        var scopes = Files.ReadOrEmpty($"{RepositoryFiles.TemplatesDirectory}/superapp-service/ServiceName.Application/ServiceNameScopes.cs");

        var added = ScopesEditor.AddConstants(scopes, "billing", ["invoice.read", "payment_method.write"]);

        Assert.Contains("public const string InvoiceRead = Prefix + \"invoice.read\";", added, StringComparison.Ordinal);
        Assert.Contains("public const string PaymentMethodWrite = Prefix + \"payment_method.write\";", added, StringComparison.Ordinal);
        Assert.Equal(added, ScopesEditor.AddConstants(added, "billing", ["invoice.read"]));
    }
}
