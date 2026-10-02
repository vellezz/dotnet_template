using System.Globalization;
using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding.Editors;

namespace SuperApp.Cli.Scaffolding.Plans;

/// <summary>Plans of <c>add service</c> and <c>remove service</c>: every place recipe 07 of the developer guide lists.</summary>
/// <remarks>
/// <para>
/// <c>add service</c> generates the projects from the template <c>Templates/superapp-service</c> of the tool with free ports and registers the service in the
/// solutions, the Migrator, the SQL bootstrap, the local realm, docker compose, Helm, the gateway policy of its experience, the
/// architecture tests and the EventId register. The first aggregate, the migration <c>Initial</c> and the client in the BFF need the
/// domain, so they are listed as next steps.
/// </para>
/// <para>
/// <c>remove service</c> undoes all of it and deletes the code, but never touches a database: the schema, role and login stay until the
/// DBA drops them in a contract-phase script (ADR-0021). It refuses while a BFF still has a client of the service.
/// </para>
/// </remarks>
internal static class ServicePlans
{
    /// <summary>Builds the plan of <c>add service</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="name">Service name in PascalCase.</param>
    /// <param name="experience">Lower-case name of the experience the service belongs to; its BFF must exist.</param>
    /// <param name="scopes">Scopes without the service prefix, e.g. <c>invoice.read</c>.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">A name is invalid or the experience has no BFF or gateway policy.</exception>
    public static ScaffoldPlan Add(RepositoryModel model, string name, string experience, IReadOnlyList<string> scopes)
    {
        Naming.EnsurePascalCase(name, "service");
        foreach (var scope in scopes)
        {
            Naming.EnsureScope(scope);
        }

        var key = name.ToLowerInvariant();
        if (model.Bffs.All(bff => bff.Key != experience))
        {
            throw new ScaffoldException($"Experience {experience} has no BFF; create it first: dotnet superapp add bff <Name>.");
        }

        var constant = GatewayEditor.PolicyConstant(model.Files.ReadOrEmpty(GatewayEditor.PoliciesPath), experience)
            ?? throw new ScaffoldException($"{GatewayEditor.PoliciesPath} has no policy for experience {experience}; create it with dotnet superapp add bff.");
        var existing = model.Services.FirstOrDefault(service => service.Name == name);
        var (apiPort, workerPort) = existing is { ApiPort: { } api, WorkerPort: { } worker } ? (api, worker) : PortAllocator.ForService(model);
        var directory = $"src/Services/{name}";
        var fullScopes = scopes.Select(scope => $"{key}.{scope}").ToList();

        List<ScaffoldStep> steps =
        [
            CommonSteps.Template("superapp-service", directory, "-n", name, "-o", directory,
                "--apiPort", apiPort.ToString(CultureInfo.InvariantCulture), "--workerPort", workerPort.ToString(CultureInfo.InvariantCulture)),
            CommonSteps.AddToSolutions(directory),
            new(MigratorEditor.ProjectPath, $"reference {name}.Infrastructure and register {name}WriteDbContext", context =>
            [
                context.Update(MigratorEditor.ProjectPath, $"ProjectReference to {name}.Infrastructure",
                    content => ProjectFileEditor.AddReference(content, $"..\\..\\Services\\{name}\\{name}.Infrastructure\\{name}.Infrastructure.csproj")),
                context.Update(MigratorEditor.ProgramPath, $"{name}WriteDbContext", content => MigratorEditor.Register(content, name)),
            ]),
            new(BootstrapSqlEditor.Path, $"schema, role and login {key}_app", context =>
                [context.Update(BootstrapSqlEditor.Path, $"(N'{key}')", content => BootstrapSqlEditor.Add(content, key))]),
        ];

        if (scopes.Count > 0)
        {
            var scopesFile = $"{directory}/{name}.Application/{name}Scopes.cs";
            steps.Add(new(scopesFile, $"scope constants {string.Join(", ", fullScopes)}", context =>
                [context.Update(scopesFile, string.Join(", ", fullScopes), content => ScopesEditor.AddConstants(content, key, scopes))]));
        }

        steps.AddRange(
        [
            new(RealmEditor.Path, $"clients {key}-api, {key}-client and scopes with audience {key}-api", context =>
                [context.Update(RealmEditor.Path, $"{key}-api, {key}-client, {fullScopes.Count} scope(s)", content => RealmEditor.AddService(content, key, fullScopes))]),
            new(ComposeFile.Path, $"{key}-api (port {apiPort}), {key}-worker; scopes requested by bff-web", context =>
            [
                context.Update(ComposeFile.Path, $"{key}-api, {key}-worker", content => ComposeEditor.AddService(content, name, key, apiPort)),
                context.Update(ComposeFile.Path, "bff-web scopes", content => fullScopes.Count == 0 ? content
                    : ComposeEditor.EditBffWebScopes(content, current => [.. current, .. fullScopes.Where(scope => !current.Contains(scope))])),
            ]),
            new(HelmValuesEditor.ServicePath(key), $"Helm values with experience: {experience}", context =>
                [context.Create(HelmValuesEditor.ServicePath(key), HelmValuesEditor.Service(key, experience), "values of superapp-service")]),
            new(GatewayEditor.PoliciesPath, $"scope prefix {key}. in the policy of {experience}", context =>
                [context.Update(GatewayEditor.PoliciesPath, $"\"{key}.\" in {constant}", content => GatewayEditor.AddScopePrefix(content, constant, $"{key}."))]),
            CommonSteps.AddArchitectureReferences(
                $"..\\..\\src\\Services\\{name}\\{name}.Api\\{name}.Api.csproj",
                $"..\\..\\src\\Services\\{name}\\{name}.Worker\\{name}.Worker.csproj"),
            CommonSteps.AllocateEventIds(name),
        ]);

        return new ScaffoldPlan($"add service {name} --experience {experience}", steps,
        [
            $"Model the first aggregate (recipe 03), then create the first migration: dotnet ef migrations add Initial -p {directory}/{name}.Infrastructure -s {directory}/{name}.Infrastructure --context {name}WriteDbContext -o Migrations (rename {{date}}_Initial.cs to Initial.cs).",
            scopes.Count == 0
                ? $"Declare the scopes in {name}Scopes and run this command again with --scope <resource.action> to register them in the realm and compose."
                : $"Describe each new scope in {name}Scopes (TODO in its summary) and hand the list over to the CIAM team (ADR-0016).",
            $"Expose the service to the module: build it (dotnet build {SolutionFiles.Slnx}, generates {directory}/{name}.Api/openapi/{name}.Api.json), then dotnet superapp add client --bff {experience} --service {name}.",
            $"Write the ADR of the context (pattern: ADR-0028) and add the context to the tables of docs/architektura.md (1.1), docs/przewodnik/04-wybor-kontekstu.md and .github/copilot-instructions.md.",
            "Run dotnet build, dotnet test and dotnet superapp doctor.",
        ]);
    }

    /// <summary>Builds the plan of <c>remove service</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="name">Service name.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The service does not exist or a BFF still has a client of it.</exception>
    public static ScaffoldPlan Remove(RepositoryModel model, string name)
    {
        var service = model.Services.FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new ScaffoldException($"There is no service {name} (dotnet superapp list services).");
        if (service.UsedByBffs.Count > 0)
        {
            throw new ScaffoldException($"BFF(s) {string.Join(", ", service.UsedByBffs)} still call {name}; remove the client first: "
                + string.Join("; ", service.UsedByBffs.Select(bff => $"dotnet superapp remove client --bff {bff} --service {name} --yes")));
        }

        var key = service.Key;
        var constant = service.Experience is null ? null : GatewayEditor.PolicyConstant(model.Files.ReadOrEmpty(GatewayEditor.PoliciesPath), service.Experience);
        List<ScaffoldStep> steps =
        [
            CommonSteps.ReleaseEventIds(name),
            CommonSteps.RemoveArchitectureReferences($"{name}.Api.csproj", $"{name}.Worker.csproj"),
        ];

        if (constant is not null)
        {
            steps.Add(new(GatewayEditor.PoliciesPath, $"scope prefix {key}. from the policy of {service.Experience}", context =>
                [context.Update(GatewayEditor.PoliciesPath, $"\"{key}.\" from {constant}", content => GatewayEditor.RemoveScopePrefix(content, constant, $"{key}."))]));
        }

        steps.AddRange(
        [
            new(HelmValuesEditor.ServicePath(key), "Helm values", context => [context.Delete(HelmValuesEditor.ServicePath(key), "values of superapp-service")]),
            new(ComposeFile.Path, $"{key}-api, {key}-worker and their scopes in bff-web", context =>
            [
                context.Update(ComposeFile.Path, $"{key}-api, {key}-worker", content => ComposeEditor.RemoveService(ComposeEditor.RemoveService(content, $"{key}-api"), $"{key}-worker")),
                context.Update(ComposeFile.Path, "bff-web scopes", content => ComposeEditor.EditBffWebScopes(content,
                    current => [.. current.Where(scope => !scope.StartsWith($"{key}.", StringComparison.Ordinal))])),
            ]),
            new(RealmEditor.Path, $"clients {key}-api, {key}-client and scopes {key}.*", context =>
                [context.Update(RealmEditor.Path, $"{key}-api, {key}-client, {key}.*", content => RealmEditor.RemoveService(content, key))]),
            new(BootstrapSqlEditor.Path, $"schema {key} from @Services (the database is not changed)", context =>
                [context.Update(BootstrapSqlEditor.Path, $"(N'{key}')", content => BootstrapSqlEditor.Remove(content, key))]),
            new(MigratorEditor.ProjectPath, $"{name}WriteDbContext and the reference to {name}.Infrastructure", context =>
            [
                context.Update(MigratorEditor.ProgramPath, $"{name}WriteDbContext", content => MigratorEditor.Unregister(content, name)),
                context.Update(MigratorEditor.ProjectPath, $"ProjectReference to {name}.Infrastructure", content => ProjectFileEditor.RemoveReference(content, $"{name}.Infrastructure.csproj")),
            ]),
            CommonSteps.RemoveFromSolutions(service.Directory),
            CommonSteps.DeleteDirectories(service.Directory),
        ]);

        return new ScaffoldPlan($"remove service {name}", steps,
        [
            $"Database (DBA, contract phase, after a backup; ADR-0021): drop the tables of schema {key}, then DROP USER {key}_app; DROP ROLE {key}_role; DROP SCHEMA {key}; and the server login {key}_app. Nothing was changed in any database.",
            $"CIAM (ADR-0016): ask the CIAM team to remove the clients {key}-api, {key}-client and the scopes {key}.*; secrets {key}-secrets in Vault.",
            $"Integration events of {name}.Contracts are gone: remove their consumers in other services and the mappings in SuperApp.AnalyticsForwarder, if any.",
            "Remove the context from the documentation (ADR status, docs/architektura.md 1.1, guide chapter 04, Copilot instructions).",
            "Run dotnet build, dotnet test and dotnet superapp doctor.",
        ]);
    }
}
