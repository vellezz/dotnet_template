using System.Globalization;
using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding.Editors;

namespace SuperApp.Cli.Scaffolding.Plans;

/// <summary>Plans of <c>add bff</c> and <c>remove bff</c>: a new experience and its BFF (recipe 11, ADR-0038).</summary>
/// <remarks>
/// <para>
/// <c>add bff</c> generates <c>src/Bff/{Name}.Bff</c> and its tests from the template <c>Templates/superapp-bff</c> of the tool and registers the experience in the
/// solutions, docker compose, Helm, the local gateway (policy, route and a gateway migration, ADR-0022), the local realm (audience and
/// internal API scope), the architecture tests and the EventId register. Domain services are added afterwards with
/// <c>add service --experience {name}</c>.
/// </para>
/// <para>
/// The gateway migration is created with <c>dotnet ef</c>, which builds the gateway; <c>--skip-migration</c> leaves it as a next step.
/// <c>remove bff</c> refuses while services still belong to the experience.
/// </para>
/// </remarks>
internal static class BffPlans
{
    private const string GatewayProject = "src/Gateway/SuperApp.Gateway";
    private static readonly MigrationTarget Gateway = new("Gateway", "GatewayDbContext", GatewayProject, "Persistence/Migrations");

    /// <summary>Builds the plan of <c>add bff</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="name">Experience name in PascalCase.</param>
    /// <param name="skipMigration">Whether to leave the gateway migration as a next step.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The name is invalid.</exception>
    public static ScaffoldPlan Add(RepositoryModel model, string name, bool skipMigration)
    {
        Naming.EnsurePascalCase(name, "experience");
        var key = name.ToLowerInvariant();
        var existing = model.Bffs.FirstOrDefault(bff => bff.Name == name);
        var port = existing?.Port ?? PortAllocator.ForBff(model);
        var directory = $"src/Bff/{name}.Bff";
        var tests = $"src/Bff/{name}.Bff.Tests";
        var migration = $"Route{name}Experience";

        List<ScaffoldStep> steps =
        [
            CommonSteps.Template("superapp-bff", directory, "-n", name, "-o", "src/Bff", "--port", port.ToString(CultureInfo.InvariantCulture)),
            CommonSteps.AddToSolutions(directory, tests),
            new(ComposeFile.Path, $"{key}-bff (port {port})", context =>
                [context.Update(ComposeFile.Path, $"{key}-bff", content => ComposeEditor.AddBff(content, name, key, port))]),
            new(HelmValuesEditor.BffPath(key), $"Helm values with experience: {key}", context =>
                [context.Create(HelmValuesEditor.BffPath(key), HelmValuesEditor.Bff(key), "values of superapp-bff")]),
            new(GatewayEditor.PoliciesPath, $"policy {name} and route /api/{key}/v{{n}}/** to {key}-bff", context =>
            [
                context.Update(GatewayEditor.PoliciesPath, $"GatewayPolicies.{name}", content => GatewayEditor.AddExperience(content, name, key)),
                context.Update(GatewayEditor.SeedPath, $"(\"{key}\", GatewayPolicies.{name})", content => GatewayEditor.AddRoute(content, key, name)),
            ]),
        ];

        if (!skipMigration)
        {
            steps.Add(MigrationPlans.Step(Gateway, migration,
                $"Routing data only (no schema change): adds the route <c>/api/{key}/v{{version:int}}/{{**rest}}</c> of the {name} experience to its BFF <c>{key}-bff</c> for both gateway profiles (ADR-0038, ADR-0039). Deploy the BFF before applying this migration."));
        }

        steps.AddRange(
        [
            new(RealmEditor.Path, $"scopes {key}-bff-audience and {key}.internal.read", context =>
                [context.Update(RealmEditor.Path, $"{key}-bff-audience, {key}.internal.read", content => RealmEditor.AddBff(content, key, name))]),
            CommonSteps.AddArchitectureReferences($"..\\..\\src\\Bff\\{name}.Bff\\{name}.Bff.csproj"),
            CommonSteps.AllocateEventIds(Component(name)),
        ]);

        List<string> next =
        [
            $"Add the domain services of the experience: dotnet superapp add service <Name> --experience {key} --scope <resource.action>, then dotnet superapp add client --bff {key} --service <Name>.",
            $"Describe the experience in an ADR (scope, services, internal API) and in docs/architektura.md; NetworkPolicy and CIAM requirements for {key} go to the infrastructure and CIAM teams (ADR-0016, ADR-0041).",
            "Run dotnet build, dotnet test and dotnet superapp doctor.",
        ];
        if (skipMigration)
        {
            next.Insert(0, $"Create the gateway migration: dotnet ef migrations add {migration} -p {GatewayProject} -s {GatewayProject} --context GatewayDbContext -o Persistence/Migrations (rename {{date}}_{migration}.cs to {migration}.cs).");
        }

        return new ScaffoldPlan($"add bff {name}", steps, next);
    }

    /// <summary>Builds the plan of <c>remove bff</c>.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="name">Experience name in PascalCase.</param>
    /// <param name="skipMigration">Whether to leave the gateway migration as a next step.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ScaffoldException">The BFF does not exist or services still belong to its experience.</exception>
    public static ScaffoldPlan Remove(RepositoryModel model, string name, bool skipMigration)
    {
        var bff = model.Bffs.FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new ScaffoldException($"There is no BFF {name}.Bff (dotnet superapp list bffs).");
        var services = model.Services.Where(service => service.Experience == bff.Key).Select(service => service.Name).ToList();
        if (services.Count > 0)
        {
            throw new ScaffoldException($"Services {string.Join(", ", services)} still belong to experience {bff.Key}; remove them first (dotnet superapp remove service <Name> --yes).");
        }

        var key = bff.Key;
        var migration = $"Remove{name}ExperienceRoute";
        List<ScaffoldStep> steps =
        [
            CommonSteps.ReleaseEventIds(Component(name)),
            CommonSteps.RemoveArchitectureReferences($"{name}.Bff.csproj"),
            new(RealmEditor.Path, $"scopes {key}-bff-audience and {key}.internal.*", context =>
                [context.Update(RealmEditor.Path, $"{key}-bff-audience, {key}.internal.*", content => RealmEditor.RemoveBff(content, key))]),
            new(GatewayEditor.SeedPath, $"route of {key}", context =>
                [context.Update(GatewayEditor.SeedPath, $"(\"{key}\", …)", content => GatewayEditor.RemoveRoute(content, key))]),
        ];

        if (!skipMigration)
        {
            steps.Add(MigrationPlans.Step(Gateway, migration,
                $"Routing data only (no schema change): removes the route, cluster and destination of the {name} experience (ADR-0039). Apply it before removing the deployment of <c>{key}-bff</c>."));
        }

        steps.AddRange(
        [
            new(GatewayEditor.PoliciesPath, $"policy {name}", context =>
                [context.Update(GatewayEditor.PoliciesPath, $"GatewayPolicies.{name}", content => GatewayEditor.RemoveExperience(content, name))]),
            new(HelmValuesEditor.BffPath(key), "Helm values", context => [context.Delete(HelmValuesEditor.BffPath(key), "values of superapp-bff")]),
            new(ComposeFile.Path, $"{key}-bff", context => [context.Update(ComposeFile.Path, $"{key}-bff", content => ComposeEditor.RemoveService(content, $"{key}-bff"))]),
            CommonSteps.RemoveFromSolutions(bff.Directory, $"{bff.Directory}.Tests"),
            CommonSteps.DeleteDirectories(bff.Directory, $"{bff.Directory}.Tests"),
        ]);

        List<string> next =
        [
            "Deploy order (expand/contract): apply the gateway migration (the route disappears) before removing the BFF deployment.",
            $"CIAM (ADR-0016): ask the CIAM team to remove the audience {key}-bff and the scopes {key}.internal.*; BFFs of other experiences calling the internal API of {key} must stop first.",
            "Run dotnet build, dotnet test and dotnet superapp doctor.",
        ];
        if (skipMigration)
        {
            next.Insert(0, $"Create the gateway migration: dotnet ef migrations add {migration} -p {GatewayProject} -s {GatewayProject} --context GatewayDbContext -o Persistence/Migrations (rename {{date}}_{migration}.cs to {migration}.cs).");
        }

        return new ScaffoldPlan($"remove bff {name}", steps, next);
    }

    private static string Component(string name) => $"`{name}.Bff` (BFF experience {name})";

}
