using System.CommandLine;
using SuperApp.Cli.Scaffolding.Plans;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp add service|bff|client</c>: creates a component and registers it everywhere it has to be (ADR-0046).</summary>
/// <remarks>
/// Every subcommand is idempotent: running it again changes nothing that is already in place, so it can also repair a half-registered
/// component. <c>--dry-run</c> lists the steps without changing anything.
/// </remarks>
internal static class AddCommand
{
    /// <summary>Creates the command with its subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>add</c> command.</returns>
    public static Command Create(CommonOptions common)
    {
        var dryRun = new Option<bool>("--dry-run") { Description = "List the steps without changing anything.", Recursive = true };
        var command = new Command("add", "Create a service, a BFF (experience), a service client of a BFF or a use case, registered everywhere it has to be.")
        {
            dryRun,
            Service(common, dryRun),
            Bff(common, dryRun),
            Client(common, dryRun),
            UseCase(common, dryRun),
        };
        foreach (var element in ElementCommands.Add(common, dryRun))
        {
            command.Subcommands.Add(element);
        }

        return command;
    }

    private static Command UseCase(CommonOptions common, Option<bool> dryRun)
    {
        var service = new Argument<string>("service") { Description = "Service, e.g. Knowledge." };
        var feature = new Argument<string>("feature") { Description = "Feature folder (usually the aggregate in plural), e.g. Categories." };
        var name = new Argument<string>("name") { Description = "Use case name, a verb phrase, e.g. ArchiveCategory or GetCategoryStats." };
        var query = new Option<bool>("--query") { Description = "Create a query (handler in Infrastructure, ADR-0026); without it a command." };
        var scope = new Option<string>("--scope") { Description = "Scope without the service prefix, already declared in {Service}Scopes, e.g. catalog.write.", Required = true };
        var dto = new Option<string>("--dto") { Description = "Name of the result DTO of a query (default: the name without Get, plus Dto)." };
        var command = new Command("usecase", "Create a vertical slice: command or query, validator, DTO, handler and a controller action that compile with TODOs to fill in.")
        {
            service,
            feature,
            name,
            query,
            scope,
            dto,
        };
        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => UseCasePlans.Add(model, parseResult.GetValue(service)!, parseResult.GetValue(feature)!, parseResult.GetValue(name)!,
                parseResult.GetValue(query), parseResult.GetValue(scope)!, parseResult.GetValue(dto)),
            apply: !parseResult.GetValue(dryRun)));
        return command;
    }

    private static Command Service(CommonOptions common, Option<bool> dryRun)
    {
        var name = new Argument<string>("name") { Description = "Service name in PascalCase, e.g. Billing." };
        var experience = new Option<string>("--experience") { Description = "Experience the service belongs to (its BFF must exist), e.g. example.", Required = true };
        var scope = new Option<string[]>("--scope")
        {
            Description = "Scopes of the service without its prefix, {resource}.{action}, e.g. invoice.read (repeatable).",
            AllowMultipleArgumentsPerToken = true,
        };
        var command = new Command("service", "Create a domain service from the template superapp-service and register it (solutions, Migrator, SQL bootstrap, realm, compose, Helm, gateway policy, architecture tests, EventId).")
        {
            name,
            experience,
            scope,
        };
        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => ServicePlans.Add(model, parseResult.GetValue(name)!, parseResult.GetValue(experience)!.ToLowerInvariant(), parseResult.GetValue(scope) ?? []),
            apply: !parseResult.GetValue(dryRun)));
        return command;
    }

    private static Command Bff(CommonOptions common, Option<bool> dryRun)
    {
        var name = new Argument<string>("name") { Description = "Experience name in PascalCase, e.g. Coaching (creates src/Bff/Coaching.Bff)." };
        var skipMigration = new Option<bool>("--skip-migration") { Description = "Do not run dotnet ef for the gateway migration; it is listed as a next step." };
        var command = new Command("bff", "Create the BFF of a new experience from the template superapp-bff and register it (solutions, compose, Helm, gateway policy, route and migration, realm, architecture tests, EventId).")
        {
            name,
            skipMigration,
        };
        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => BffPlans.Add(model, parseResult.GetValue(name)!, parseResult.GetValue(skipMigration)),
            apply: !parseResult.GetValue(dryRun)));
        return command;
    }

    private static Command Client(CommonOptions common, Option<bool> dryRun)
    {
        var bff = new Option<string>("--bff") { Description = "Experience of the BFF, e.g. example.", Required = true };
        var service = new Option<string>("--service") { Description = "Service of the same experience, e.g. Billing.", Required = true };
        var command = new Command("client", "Generate a client of a domain service in the BFF of its experience (Refitter) and register it (Program.cs, appsettings, compose, Helm).")
        {
            bff,
            service,
        };
        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => ClientPlans.Add(model, parseResult.GetValue(bff)!, parseResult.GetValue(service)!),
            apply: !parseResult.GetValue(dryRun)));
        return command;
    }
}
