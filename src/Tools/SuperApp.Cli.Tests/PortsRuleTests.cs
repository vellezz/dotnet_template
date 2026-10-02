using SuperApp.Cli.Doctor;
using SuperApp.Cli.Doctor.Rules;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Tests;

/// <summary>A port may be shared by the launch profile and the compose service of one component, never by two components.</summary>
public sealed class PortsRuleTests
{
    private const string Compose = """
        services:
          knowledge-api:
            image: knowledge
            ports:
              - "5101:8080"
            environment:
              Authentication__Authority: http://keycloak:8080/realms/superapp
        """;

    [Fact]
    public void Ide_and_container_of_one_component_share_a_port()
    {
        using var repository = new TestRepository()
            .Write(ComposeFile.Path, Compose)
            .LaunchProfile("src/Services/Knowledge/Knowledge.Api", "knowledge-api", "http://localhost:5101");

        Assert.Empty(new PortsRule().Check(repository.Scan(), new DoctorSettings([])));
    }

    [Fact]
    public void Two_components_on_one_port_are_reported()
    {
        using var repository = new TestRepository()
            .Write(ComposeFile.Path, Compose)
            .LaunchProfile("src/Services/Billing/Billing.Api", "billing-api", "http://localhost:5101");

        var finding = Assert.Single(new PortsRule().Check(repository.Scan(), new DoctorSettings([])));
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("billing-api", finding.Message, StringComparison.Ordinal);
        Assert.Contains("knowledge-api", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_services_ports_and_environment_are_read()
    {
        var service = Assert.Single(ComposeFile.Read(Compose));

        Assert.Equal("knowledge-api", service.Name);
        Assert.Equal([5101], service.HostPorts);
        Assert.Equal(["Authentication__Authority"], service.Environment);
    }
}
