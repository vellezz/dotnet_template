using System.CommandLine;
using System.Globalization;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp list services|bffs|experiences|scopes|ports|eventids</c>: an overview of the repository in one table.</summary>
/// <remarks>
/// Answers the questions asked before adding something: which ports and event ID ranges are free, which scopes exist, which services
/// belong to which experience. With <c>--json</c> the same data as an array, for scripts and assistants.
/// </remarks>
internal static class ListCommand
{
    private static readonly string[] Targets = ["services", "bffs", "experiences", "scopes", "ports", "eventids"];

    /// <summary>Creates the command.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>list</c> command.</returns>
    public static Command Create(CommonOptions common)
    {
        var what = new Argument<string>("what") { Description = $"What to list: {string.Join(", ", Targets)}." };
        what.AcceptOnlyFromAmong(Targets);

        var command = new Command("list", "List services, BFFs, experiences, scopes, local ports or event ID ranges.") { what };
        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var model = RepositoryScanner.Scan(root);
            switch (parseResult.GetValue(what))
            {
                case "services":
                    Write(output, ["Service", "Experience", "Api port", "Worker port", "Scopes", "Used by BFF"],
                        model.Services.Select(service => (object)new { service.Name, service.Experience, service.ApiPort, service.WorkerPort, service.Scopes, usedByBffs = service.UsedByBffs }),
                        model.Services.Select(service => Row(service.Name, service.Experience, service.ApiPort, service.WorkerPort, service.Scopes.Count, string.Join(", ", service.UsedByBffs))));
                    break;
                case "bffs":
                    Write(output, ["BFF", "Experience", "Port", "Clients", "Internal scopes"],
                        model.Bffs.Select(bff => (object)new { bff.Project, experience = bff.Key, bff.Port, bff.Clients, bff.Scopes }),
                        model.Bffs.Select(bff => Row(bff.Project, bff.Key, bff.Port, string.Join(", ", bff.Clients), string.Join(", ", bff.Scopes))));
                    break;
                case "experiences":
                    var experiences = model.Experiences.Select(experience => new
                    {
                        name = experience,
                        bff = model.Bffs.FirstOrDefault(bff => bff.Key == experience)?.Project,
                        services = model.Services.Where(service => service.Experience == experience).Select(service => service.Name).ToList(),
                    }).ToList();
                    Write(output, ["Experience", "BFF", "Services"], experiences,
                        experiences.Select(experience => Row(experience.name, experience.bff, string.Join(", ", experience.services))));
                    break;
                case "scopes":
                    var scopes = model.Services.SelectMany(service => service.Scopes.Select(scope => new { scope, owner = service.Name }))
                        .Concat(model.Bffs.SelectMany(bff => bff.Scopes.Select(scope => new { scope, owner = bff.Project })))
                        .OrderBy(item => item.scope, StringComparer.Ordinal).ToList();
                    Write(output, ["Scope", "Declared by"], scopes, scopes.Select(item => Row(item.scope, item.owner)));
                    break;
                case "ports":
                    var ports = model.Ports.OrderBy(port => port.Port).ThenBy(port => port.Owner, StringComparer.Ordinal).ToList();
                    Write(output, ["Port", "Component", "Source"], ports, ports.Select(port => Row(port.Port, port.Owner, port.Source)));
                    break;
                default:
                    var ranges = model.EventIdRanges.Select(range => new
                    {
                        range.Start,
                        range.End,
                        range.Component,
                        used = model.EventIds.Where(usage => range.Contains(usage.Id)).Select(usage => usage.Id).Order().ToList(),
                    }).ToList();
                    Write(output, ["Range", "Used", "Next free", "Component"], ranges,
                        ranges.Select(range => Row($"{range.Start}–{range.End}", range.used.Count, NextFree(range.Start, range.End, range.used), range.Component)));
                    break;
            }

            return ExitCodes.Success;
        });

        return command;
    }

    private static string NextFree(int start, int end, List<int> used)
    {
        var next = used.Count == 0 ? start + 1 : used.Max() + 1;
        return next <= end ? next.ToString(CultureInfo.InvariantCulture) : "full";
    }

    private static string[] Row(params object?[] cells) =>
        [.. cells.Select(cell => cell switch
        {
            null => "-",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => cell.ToString() ?? "-",
        })];

    private static void Write(OutputWriter output, IReadOnlyList<string> headers, IEnumerable<object> json, IEnumerable<IReadOnlyList<string>> rows)
    {
        if (output.IsJson)
        {
            output.WriteJson(json);
        }
        else
        {
            output.Table(headers, rows);
        }
    }
}
