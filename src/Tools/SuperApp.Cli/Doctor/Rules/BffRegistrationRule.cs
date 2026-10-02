using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor.Rules;

/// <summary>Every BFF is registered where a new experience has to be added, and every service client it has is configured (recipe 11).</summary>
/// <remarks>
/// <para>
/// For each BFF: the local docker compose (<c>{key}-bff</c>), the Helm values of the <c>superapp-bff</c> chart, the gateway policy and the
/// route seed of the local edge gateway (ADR-0037, ADR-0039), the architecture tests, the audience scope in the local realm and a range
/// in the EventId register.
/// </para>
/// <para>
/// For each generated client in <c>Clients/{Service}</c>: the registration <c>AddDownstreamApi&lt;I{Service}Api&gt;</c> in <c>Program.cs</c>
/// and the address of the service in <c>appsettings.json</c>, <c>appsettings.Development.json</c>, the compose environment and the Helm
/// values. A client without an address fails at startup (<c>Downstream:{Service}:BaseAddress</c> is required).
/// </para>
/// </remarks>
internal sealed class BffRegistrationRule : IDoctorRule
{
    private const string GatewayPolicies = "src/Gateway/SuperApp.Gateway/Security/GatewayPolicies.cs";
    private const string RouteSeed = "src/Gateway/SuperApp.Gateway/Persistence/Seed/ProxyConfigurationSeed.cs";
    private const string ArchitectureTests = "tests/SuperApp.ArchitectureTests/SuperApp.ArchitectureTests.csproj";
    private const string Realm = "deploy/local/keycloak/realm-superapp.json";

    /// <inheritdoc />
    public string Id => "bff-registration";

    /// <inheritdoc />
    public string Description => "Every BFF is in docker compose, Helm and the gateway; every service client of a BFF is registered and has an address.";

    /// <inheritdoc />
    public IEnumerable<Finding> Check(RepositoryModel model, DoctorSettings settings)
    {
        var files = model.Files;
        var policies = files.ReadOrEmpty(GatewayPolicies);
        var seed = files.ReadOrEmpty(RouteSeed);
        var architectureTests = files.ReadOrEmpty(ArchitectureTests);
        var realm = files.ReadOrEmpty(Realm);

        foreach (var bff in model.Bffs)
        {
            var container = model.Compose.FirstOrDefault(service => service.Name == $"{bff.Key}-bff");
            if (container is null)
            {
                yield return Missing(bff, ComposeFile.Path, $"the service {bff.Key}-bff (copy the block of example-bff)");
            }

            var values = $"deploy/helm/superapp-bff/values-{bff.Key}.yaml";
            var valuesContent = files.ReadOrEmpty(values);
            if (valuesContent.Length == 0)
            {
                yield return Missing(bff, values, $"the file, with experience: {bff.Key}, secretName and downstream");
            }

            if (!policies.Contains($"const string {bff.Name} =", StringComparison.Ordinal))
            {
                yield return Missing(bff, GatewayPolicies, $"public const string {bff.Name} = \"{bff.Key}\" and its policy");
            }

            if (!seed.Contains($"(\"{bff.Key}\",", StringComparison.Ordinal))
            {
                yield return Missing(bff, RouteSeed, $"(\"{bff.Key}\", GatewayPolicies.{bff.Name}) in Experiences and a new gateway migration");
            }

            if (!architectureTests.Contains($"{bff.Project}.csproj", StringComparison.Ordinal))
            {
                yield return Missing(bff, ArchitectureTests, $"a ProjectReference to {bff.Project}.csproj (rules 12–14 cover only referenced BFFs)");
            }

            if (realm.Length > 0 && !realm.Contains($"\"{bff.Key}-bff-audience\"", StringComparison.Ordinal))
            {
                yield return Missing(bff, Realm, $"the client scope {bff.Key}-bff-audience (audience {bff.Key}-bff), default for bff-web, mobile-* and dev-cli");
            }

            if (bff.EventIdRanges.Count == 0)
            {
                yield return Missing(bff, EventIdRegister.Path, $"a row with the next free range of 1000 IDs for {bff.Project}");
            }

            var program = files.ReadOrEmpty($"{bff.Directory}/Program.cs");
            var settingsFile = files.ReadOrEmpty($"{bff.Directory}/appsettings.json");
            var developmentSettings = files.ReadOrEmpty($"{bff.Directory}/appsettings.Development.json");
            foreach (var client in bff.Clients)
            {
                if (!program.Contains($"AddDownstreamApi<I{client}Api>", StringComparison.Ordinal))
                {
                    yield return Missing(bff, $"{bff.Directory}/Program.cs", $"builder.Services.AddDownstreamApi<I{client}Api>(builder.Configuration, \"{client}\").AddUserTokenForwarding();");
                }

                foreach (var (file, content) in ((string File, string Content)[])[($"{bff.Directory}/appsettings.json", settingsFile), ($"{bff.Directory}/appsettings.Development.json", developmentSettings), (values, valuesContent)])
                {
                    if (content.Length > 0 && !content.Contains($"{client}\":", StringComparison.Ordinal) && !content.Contains($"{client}:", StringComparison.Ordinal))
                    {
                        yield return Missing(bff, file, $"the address of {client} under Downstream");
                    }
                }

                if (container is not null && !container.Environment.Contains($"Downstream__{client}__BaseAddress"))
                {
                    yield return Missing(bff, ComposeFile.Path, $"Downstream__{client}__BaseAddress in the environment of {bff.Key}-bff");
                }
            }
        }
    }

    private Finding Missing(BffInfo bff, string file, string what) =>
        new(Id, Severity.Error, $"BFF {bff.Project} is not registered in {file}.", file, Fix: $"Add {what}.");
}
