using System.CommandLine;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp info &lt;name&gt;</c>: everything the repository says about one service or one BFF.</summary>
/// <remarks>
/// The name is matched without regard to case against service names (<c>Knowledge</c>), experience names (<c>example</c>) and BFF project
/// names (<c>Example.Bff</c>). Shows ports, experience, scopes, event ID ranges, integration events, consumers and the BFFs that call the
/// service (or the services a BFF calls).
/// </remarks>
internal static class InfoCommand
{
    /// <summary>Creates the command.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>info</c> command.</returns>
    public static Command Create(CommonOptions common)
    {
        var name = new Argument<string>("name") { Description = "Service (e.g. Knowledge), experience (example) or BFF (Example.Bff)." };
        var command = new Command("info", "Show everything about one service or BFF: ports, experience, scopes, event IDs, events, consumers, clients.") { name };
        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var model = RepositoryScanner.Scan(root);
            var wanted = parseResult.GetValue(name) ?? string.Empty;
            var service = model.Services.FirstOrDefault(candidate => string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase));
            var bff = model.Bffs.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.Project, wanted, StringComparison.OrdinalIgnoreCase));

            if (service is not null)
            {
                WriteService(output, service);
            }
            else if (bff is not null)
            {
                WriteBff(output, bff);
            }
            else
            {
                output.Error($"No service or BFF named {wanted}. Known: {string.Join(", ", model.Services.Select(s => s.Name).Concat(model.Bffs.Select(b => b.Project)))}.");
                return ExitCodes.NotFound;
            }

            return ExitCodes.Success;
        });

        return command;
    }

    private static void WriteService(OutputWriter output, ServiceInfo service)
    {
        if (output.IsJson)
        {
            output.WriteJson(new { kind = "service", service.Name, service.Key, service.Directory, service.Experience, service.ApiPort, service.WorkerPort, service.Scopes,
                eventIdRanges = service.EventIdRanges.Select(range => $"{range.Start}-{range.End}"), service.IntegrationEvents, service.Consumers, service.UsedByBffs });
            return;
        }

        output.Line($"Service      {service.Name} ({service.Directory})");
        output.Line($"Experience   {service.Experience ?? "- (missing in Helm values)"}");
        output.Line($"Ports        api {service.ApiPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}, worker {service.WorkerPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}");
        output.Line($"Schema       {service.Key} (login {service.Key}_app)");
        output.Line($"Event IDs    {Join(service.EventIdRanges.Select(range => $"{range.Start}–{range.End}"))}");
        output.Line($"Scopes       {Join(service.Scopes)}");
        output.Line($"Events       {Join(service.IntegrationEvents)}");
        output.Line($"Consumers    {Join(service.Consumers)}");
        output.Line($"Used by BFF  {Join(service.UsedByBffs)}");
    }

    private static void WriteBff(OutputWriter output, BffInfo bff)
    {
        if (output.IsJson)
        {
            output.WriteJson(new { kind = "bff", bff.Project, experience = bff.Key, bff.Directory, bff.Port, bff.Clients, bff.Scopes,
                eventIdRanges = bff.EventIdRanges.Select(range => $"{range.Start}-{range.End}") });
            return;
        }

        output.Line($"BFF          {bff.Project} ({bff.Directory})");
        output.Line($"Experience   {bff.Key} (route /api/{bff.Key}/v{{n}}/**)");
        output.Line($"Port         {bff.Port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}");
        output.Line($"Clients      {Join(bff.Clients)}");
        output.Line($"Scopes       {Join(bff.Scopes)}");
        output.Line($"Event IDs    {Join(bff.EventIdRanges.Select(range => $"{range.Start}–{range.End}"))}");
    }

    private static string Join(IEnumerable<string> items) => items.Any() ? string.Join(", ", items) : "-";
}
