using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor.Rules;

/// <summary>Every domain service is registered everywhere a new service has to be added (recipe 07 of the developer guide).</summary>
/// <remarks>
/// Checks, for each service: the Migrator (project reference and <c>{Name}WriteDbContext</c>), the bootstrap script of the database
/// (schema, role and login, ADR-0021), the local docker compose (<c>{key}-api</c>, <c>{key}-worker</c>), the Helm values with the
/// required <c>experience</c> (ADR-0041), the architecture tests, the scope prefix in the gateway policy of its experience and a range in
/// the EventId register. Each finding names the file and what to add.
/// </remarks>
internal sealed class ServiceRegistrationRule : IDoctorRule
{
    private const string MigratorProject = "src/Migrator/SuperApp.Migrator/SuperApp.Migrator.csproj";
    private const string MigratorProgram = "src/Migrator/SuperApp.Migrator/Program.cs";
    private const string Bootstrap = "deploy/sql/01-bootstrap.sql";
    private const string ArchitectureTests = "tests/SuperApp.ArchitectureTests/SuperApp.ArchitectureTests.csproj";
    private const string GatewayPolicies = "src/Gateway/SuperApp.Gateway/Security/GatewayPolicies.cs";

    /// <inheritdoc />
    public string Id => "service-registration";

    /// <inheritdoc />
    public string Description => "Every service is in the Migrator, the SQL bootstrap, docker compose, Helm, the architecture tests, the gateway policy and the EventId register.";

    /// <inheritdoc />
    public IEnumerable<Finding> Check(RepositoryModel model, DoctorSettings settings)
    {
        var files = model.Files;
        var migratorProject = files.ReadOrEmpty(MigratorProject);
        var migratorProgram = files.ReadOrEmpty(MigratorProgram);
        var bootstrap = files.ReadOrEmpty(Bootstrap);
        var architectureTests = files.ReadOrEmpty(ArchitectureTests);
        var policies = files.ReadOrEmpty(GatewayPolicies);
        var compose = model.Compose.Select(service => service.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var service in model.Services)
        {
            var name = service.Name;
            var key = service.Key;

            if (!migratorProject.Contains($"{name}.Infrastructure.csproj", StringComparison.Ordinal))
            {
                yield return Missing(name, MigratorProject, $"a ProjectReference to {service.Directory}/{name}.Infrastructure/{name}.Infrastructure.csproj");
            }

            if (!migratorProgram.Contains($"{name}WriteDbContext", StringComparison.Ordinal))
            {
                yield return Missing(name, MigratorProgram, $"AddDbContext<{name}WriteDbContext>(...) and typeof({name}WriteDbContext) in the list of contexts");
            }

            if (!bootstrap.Contains($"(N'{key}')", StringComparison.Ordinal))
            {
                yield return Missing(name, Bootstrap, $"the row (N'{key}') in @Services");
            }

            foreach (var container in (string[])[$"{key}-api", $"{key}-worker"])
            {
                if (!compose.Contains(container))
                {
                    yield return Missing(name, ComposeFile.Path, $"the service {container} (copy the block of an existing service)");
                }
            }

            var values = $"deploy/helm/superapp-service/values-{key}.yaml";
            if (!files.Exists(values))
            {
                yield return Missing(name, values, "the file, with service, secretName and experience");
            }
            else if (service.Experience is null)
            {
                yield return Missing(name, values, "experience: <experience> (required, ADR-0041)");
            }

            foreach (var project in (string[])[$"{name}.Api.csproj", $"{name}.Worker.csproj"])
            {
                if (!architectureTests.Contains(project, StringComparison.Ordinal))
                {
                    yield return Missing(name, ArchitectureTests, $"a ProjectReference to {project}");
                }
            }

            if (!policies.Contains($"\"{key}.\"", StringComparison.Ordinal))
            {
                yield return Missing(name, GatewayPolicies, $"HasScopeOf(context.User, \"{key}.\") in the policy of experience {service.Experience ?? "?"}");
            }

            if (service.EventIdRanges.Count == 0)
            {
                yield return Missing(name, EventIdRegister.Path, $"a row with the next free range of 1000 IDs for {name}");
            }
        }
    }

    private Finding Missing(string service, string file, string what) =>
        new(Id, Severity.Error, $"Service {service} is not registered in {file}.", file, Fix: $"Add {what}.");
}
