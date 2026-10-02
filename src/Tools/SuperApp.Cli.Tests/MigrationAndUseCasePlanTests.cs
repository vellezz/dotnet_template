using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding;
using SuperApp.Cli.Scaffolding.Plans;

namespace SuperApp.Cli.Tests;

/// <summary>Migration targets follow the Migrator; <c>migration add</c> and <c>add usecase</c> check their preconditions and plan the right files.</summary>
public sealed class MigrationAndUseCasePlanTests
{
    private static readonly RepositoryModel Model = RepositoryScanner.Scan(RepositoryRoot.Find(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Tests must run inside the repository."));

    [Fact]
    public void Migration_targets_are_in_the_order_of_the_migrator()
    {
        var targets = MigrationTargets.All(Model);

        Assert.Equal(["Gateway", "Knowledge", "SleepDiary"], targets.Select(target => target.Name));
        Assert.Equal("src/Services/Knowledge/Knowledge.Infrastructure/Migrations", targets[1].MigrationsPath);
        Assert.Contains(MigrationTargets.Migrations(Model.Files, targets[0]), id => id.EndsWith("_RouteExperienceThroughBff", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Knowledge", "Initial")]
    [InlineData("NoSuchContext", "AddThing")]
    [InlineData("Knowledge", "add_thing")]
    public void Migration_add_refuses_existing_names_unknown_contexts_and_invalid_names(string context, string name) =>
        Assert.Throws<ScaffoldException>(() => MigrationPlans.Add(Model, context, name));

    [Fact]
    public void Query_use_case_puts_its_handler_into_infrastructure()
    {
        var plan = UseCasePlans.Add(Model, "Knowledge", "Reports", "GetCatalogStats", query: true, "catalog.read", dto: null);

        Assert.Contains(plan.Steps, step => step.Target == "src/Services/Knowledge/Knowledge.Infrastructure/Features/Reports/GetCatalogStatsHandler.cs");
        Assert.Contains(plan.Steps, step => step.Description.Contains("returning CatalogStatsDto", StringComparison.Ordinal));
        Assert.Contains(plan.Steps, step => step.Target == "src/Services/Knowledge/Knowledge.Api/Controllers/ReportsController.cs");
    }

    [Theory]
    [InlineData("Knowledge", "Categories", "ArchiveCategory", "nope.write")]
    [InlineData("Knowledge", "Categories", "CreateCategory", "catalog.write")]
    [InlineData("NoSuchService", "Things", "DoThing", "thing.write")]
    public void Add_usecase_refuses_undeclared_scopes_existing_use_cases_and_unknown_services(string service, string feature, string name, string scope) =>
        Assert.Throws<ScaffoldException>(() => UseCasePlans.Add(Model, service, feature, name, query: false, scope, dto: null));
}
