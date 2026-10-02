using System.CommandLine;
using SuperApp.Cli.Scaffolding.Plans;

namespace SuperApp.Cli.Commands;

/// <summary>
/// Subcommands of <c>add</c> and <c>remove</c> for domain and cross-cutting elements: <c>aggregate</c>, <c>event</c>, <c>consumer</c>,
/// <c>scope</c>, <c>flag</c>, <c>product-event</c> (ADR-0046).
/// </summary>
/// <remarks>
/// Each pair shares its arguments; <c>add</c> runs unless <c>--dry-run</c> is given (the option of the parent command), <c>remove</c> runs only
/// with <c>--yes</c> and refuses while other code still uses the element.
/// </remarks>
internal static class ElementCommands
{
    /// <summary>Creates the <c>add</c> subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <param name="dryRun">The <c>--dry-run</c> option of <c>add</c>.</param>
    /// <returns>The subcommands.</returns>
    public static IEnumerable<Command> Add(CommonOptions common, Option<bool> dryRun) => Create(common, dryRun, remove: false);

    /// <summary>Creates the <c>remove</c> subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <param name="yes">The <c>--yes</c> option of <c>remove</c>.</param>
    /// <returns>The subcommands.</returns>
    public static IEnumerable<Command> Remove(CommonOptions common, Option<bool> yes) => Create(common, yes, remove: true);

    private static IEnumerable<Command> Create(CommonOptions common, Option<bool> switchOption, bool remove)
    {
        bool Apply(ParseResult parseResult) => remove ? parseResult.GetValue(switchOption) : !parseResult.GetValue(switchOption);

        {
            var service = new Argument<string>("service") { Description = "Service, e.g. Billing." };
            var feature = new Argument<string>("feature") { Description = "Domain folder, e.g. Invoices." };
            var aggregate = new Argument<string>("aggregate") { Description = "Aggregate name, e.g. Invoice." };
            var table = new Option<string>("--table") { Description = "Table name (default: the plural of the aggregate)." };
            var command = new Command("aggregate", remove
                ? "Remove an aggregate with its ID, errors, repository, EF configuration and test (refused while other code uses it)."
                : "Create an aggregate with its strongly typed ID, errors, repository port and implementation, EF configuration and a domain test.")
            {
                service,
                feature,
                aggregate,
            };
            if (!remove)
            {
                command.Options.Add(table);
            }

            command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common, model => remove
                ? DomainPlans.RemoveAggregate(model, parseResult.GetValue(service)!, parseResult.GetValue(feature)!, parseResult.GetValue(aggregate)!)
                : DomainPlans.AddAggregate(model, parseResult.GetValue(service)!, parseResult.GetValue(feature)!, parseResult.GetValue(aggregate)!, parseResult.GetValue(table)),
                Apply(parseResult)));
            yield return command;
        }

        {
            var service = new Argument<string>("service") { Description = "Service, e.g. Billing." };
            var aggregate = new Argument<string>("aggregate") { Description = "Aggregate that raises the event, e.g. Invoice." };
            var name = new Argument<string>("name") { Description = "Event name in the past tense, e.g. InvoiceIssued." };
            var integration = new Option<bool>("--integration") { Description = "Also create the integration event {Name}V1 in Contracts and its translator (outbox)." };
            var command = new Command("event", remove
                ? "Remove a domain event with its integration event and translator (refused while other code uses them)."
                : "Create a domain event; with --integration also the versioned integration event and the translator that publishes it.")
            {
                service,
                aggregate,
                name,
            };
            if (!remove)
            {
                command.Options.Add(integration);
            }

            command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common, model => remove
                ? DomainPlans.RemoveEvent(model, parseResult.GetValue(service)!, parseResult.GetValue(aggregate)!, parseResult.GetValue(name)!)
                : DomainPlans.AddEvent(model, parseResult.GetValue(service)!, parseResult.GetValue(aggregate)!, parseResult.GetValue(name)!, parseResult.GetValue(integration)),
                Apply(parseResult)));
            yield return command;
        }

        {
            var service = new Argument<string>("service") { Description = "Service whose Worker consumes the event, e.g. Knowledge." };
            var contract = new Option<string>("--event") { Description = "Integration event type, e.g. InvoiceIssuedV1.", Required = true };
            var command = new Command("consumer", remove
                ? "Remove a consumer from the Worker (and the reference to the publisher's Contracts when unused)."
                : "Create a consumer of an integration event in the Worker of a service, with the reference to the publisher's Contracts.")
            {
                service,
                contract,
            };
            command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common, model => remove
                ? DomainPlans.RemoveConsumer(model, parseResult.GetValue(service)!, parseResult.GetValue(contract)!)
                : DomainPlans.AddConsumer(model, parseResult.GetValue(service)!, parseResult.GetValue(contract)!),
                Apply(parseResult)));
            yield return command;
        }

        {
            var service = new Argument<string>("service") { Description = "Service, e.g. Billing." };
            var scope = new Argument<string>("scope") { Description = "Scope without the service prefix, {resource}.{action}, e.g. invoice.read." };
            var command = new Command("scope", remove
                ? "Remove a scope: constant, local realm and bff-web (refused while commands or queries require it)."
                : "Declare a scope: constant in {Service}Scopes, client scope in the local realm, requested by bff-web.")
            {
                service,
                scope,
            };
            command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common, model => remove
                ? ElementPlans.RemoveScope(model, parseResult.GetValue(service)!, parseResult.GetValue(scope)!)
                : ElementPlans.AddScope(model, parseResult.GetValue(service)!, parseResult.GetValue(scope)!),
                Apply(parseResult)));
            yield return command;
        }

        {
            var service = new Argument<string>("service") { Description = "Service, e.g. Billing." };
            var name = new Argument<string>("name") { Description = "Flag name in snake_case without the service prefix, e.g. invoice_reminders." };
            var defaultValue = new Option<bool>("--default-on") { Description = "Default value true (a kill switch of an existing feature); without it false (a new feature)." };
            var command = new Command("flag", remove
                ? "Remove a feature flag (refused while code reads it)."
                : "Declare a feature flag in {Service}FeatureFlags (ADR-0036).")
            {
                service,
                name,
            };
            if (!remove)
            {
                command.Options.Add(defaultValue);
            }

            command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common, model => remove
                ? ElementPlans.RemoveFlag(model, parseResult.GetValue(service)!, parseResult.GetValue(name)!)
                : ElementPlans.AddFlag(model, parseResult.GetValue(service)!, parseResult.GetValue(name)!, parseResult.GetValue(defaultValue)),
                Apply(parseResult)));
            yield return command;
        }

        {
            var contract = new Argument<string>("event") { Description = "Integration event type, e.g. InvoiceIssuedV1." };
            var name = new Option<string>("--name") { Description = "Product event name in snake_case (default: {service}_{event})." };
            var command = new Command("product-event", remove
                ? "Remove the mapping of an integration event to a product event from the analytics forwarder."
                : "Map an integration event to a product event in the analytics forwarder (ADR-0036): consumer, name and Contracts reference.")
            {
                contract,
            };
            if (!remove)
            {
                command.Options.Add(name);
            }

            command.SetAction(parseResult => ScaffoldRunner.Run(parseResult, common, model => remove
                ? ElementPlans.RemoveProductEvent(model, parseResult.GetValue(contract)!)
                : ElementPlans.AddProductEvent(model, parseResult.GetValue(contract)!, parseResult.GetValue(name)),
                Apply(parseResult)));
            yield return command;
        }
    }
}
