using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding;
using SuperApp.Cli.Scaffolding.Plans;

namespace SuperApp.Cli.Tests;

/// <summary>Plans check their preconditions before any change and list their steps without applying them.</summary>
public sealed class ScaffoldPlanTests
{
    private static readonly RepositoryModel Model = RepositoryScanner.Scan(RepositoryRoot.Find(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Tests must run inside the repository."));

    [Fact]
    public void Add_service_plan_lists_every_registration_and_free_ports()
    {
        var plan = ServicePlans.Add(Model, "Billing", "example", ["invoice.read"]);
        var report = plan.Run(new ScaffoldContext(Model.Files, new ProcessRunner(Model.Files.Root)), apply: false);

        Assert.All(report.Steps, step => Assert.Equal(StepStatus.Planned, step.Status));
        Assert.Contains(report.Steps, step => step.Detail.Contains("--apiPort 5103 --workerPort 5113", StringComparison.Ordinal));
        string[] targets = [SolutionFiles.Slnx, "deploy/sql/01-bootstrap.sql", ComposeFile.Path, EventIdRegister.Path, "deploy/helm/superapp-service/values-billing.yaml"];
        Assert.All(targets, target => Assert.Contains(report.Steps, step => step.Target == target));
    }

    [Theory]
    [InlineData("billing", "example")]
    [InlineData("Billing", "nosuchexperience")]
    [InlineData("SuperAppTools", "example")]
    public void Add_service_refuses_invalid_names_and_unknown_experiences(string name, string experience) =>
        Assert.Throws<ScaffoldException>(() => ServicePlans.Add(Model, name, experience, []));

    [Fact]
    public void Remove_service_refuses_while_a_bff_calls_it()
    {
        var exception = Assert.Throws<ScaffoldException>(() => ServicePlans.Remove(Model, "Knowledge"));

        Assert.Contains("remove client --bff example --service Knowledge", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_bff_refuses_while_services_belong_to_its_experience() =>
        Assert.Throws<ScaffoldException>(() => BffPlans.Remove(Model, "Example", skipMigration: true));

    [Fact]
    public void Remove_client_refuses_while_controllers_use_it()
    {
        var exception = Assert.Throws<ScaffoldException>(() => ClientPlans.Remove(Model, "example", "Knowledge"));

        Assert.Contains("Controllers", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_client_only_for_a_service_of_the_same_experience()
    {
        var services = Model.Services.Select(service => service with { Experience = "other" }).ToList();

        Assert.Throws<ScaffoldException>(() => ClientPlans.Add(Model with { Services = services }, "example", "Knowledge"));
    }
}
