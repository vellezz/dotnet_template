using System.CommandLine;
using SuperApp.Cli.Scaffolding.Plans;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp remove service|bff|client</c>: undoes <c>add</c> and deletes the code (ADR-0046).</summary>
/// <remarks>
/// Without <c>--yes</c> only the plan is shown. Removal refuses while something still depends on the component (a BFF client of a service,
/// services of an experience, controllers using a client) and never changes a database or the CIAM: what has to happen there is listed
/// as next steps for the DBA and the CIAM team.
/// </remarks>
internal static class RemoveCommand
{
    /// <summary>Creates the command with its subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>remove</c> command.</returns>
    public static Command Create(CommonOptions common)
    {
        var yes = new Option<bool>("--yes") { Description = "Apply the removal; without it only the plan is shown.", Recursive = true };
        var command = new Command("remove", "Remove a service, a BFF (experience), a service client of a BFF or a domain element, with its registrations and its code.")
        {
            yes,
            Service(common, yes),
            Bff(common, yes),
            Client(common, yes),
        };
        command.Subcommands.Add(UseCase(common, yes));
        foreach (var element in ElementCommands.Remove(common, yes))
        {
            command.Subcommands.Add(element);
        }

        return command;
    }

    private static Command UseCase(CommonOptions common, Option<bool> yes)
    {
        var service = new Argument<string>("service") { Description = "Service, e.g. Knowledge." };
        var feature = new Argument<string>("feature") { Description = "Feature folder, e.g. Categories." };
        var name = new Argument<string>("name") { Description = "Use case name, e.g. ArchiveCategory." };
        var command = new Command("usecase", "Remove a use case: its slice, query handler and controller action (refused while other code uses it).")
        {
            service,
            feature,
            name,
        };
        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => UseCasePlans.Remove(model, parseResult.GetValue(service)!, parseResult.GetValue(feature)!, parseResult.GetValue(name)!), apply: parseResult.GetValue(yes)));
        return command;
    }

    private static Command Service(CommonOptions common, Option<bool> yes)
    {
        var name = new Argument<string>("name") { Description = "Service name, e.g. Billing." };
        var command = new Command("service", "Remove a domain service and its registrations; the database schema stays (steps for the DBA are listed).") { name };
        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => ServicePlans.Remove(model, parseResult.GetValue(name)!), apply: parseResult.GetValue(yes)));
        return command;
    }

    private static Command Bff(CommonOptions common, Option<bool> yes)
    {
        var name = new Argument<string>("name") { Description = "Experience name in PascalCase, e.g. Coaching." };
        var skipMigration = new Option<bool>("--skip-migration") { Description = "Do not run dotnet ef for the gateway migration; it is listed as a next step." };
        var command = new Command("bff", "Remove the BFF of an experience (its services must be removed first) and its gateway route, with a gateway migration.")
        {
            name,
            skipMigration,
        };
        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => BffPlans.Remove(model, parseResult.GetValue(name)!, parseResult.GetValue(skipMigration)), apply: parseResult.GetValue(yes)));
        return command;
    }

    private static Command Client(CommonOptions common, Option<bool> yes)
    {
        var bff = new Option<string>("--bff") { Description = "Experience of the BFF, e.g. example.", Required = true };
        var service = new Option<string>("--service") { Description = "Service whose client is removed.", Required = true };
        var command = new Command("client", "Remove a generated service client from a BFF (refused while controllers still use it).")
        {
            bff,
            service,
        };
        command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common,
            model => ClientPlans.Remove(model, parseResult.GetValue(bff)!, parseResult.GetValue(service)!), apply: parseResult.GetValue(yes)));
        return command;
    }
}
