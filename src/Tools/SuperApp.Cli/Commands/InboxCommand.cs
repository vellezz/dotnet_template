using System.CommandLine;
using SuperApp.Cli.LocalEnvironment;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp inbox status|list|clean</c>: inspects and manages MassTransit consumer inbox state (ADR-0021, ADR-0035).</summary>
/// <remarks>
/// Queries the transactional InboxState tables across service schemas in MSSQL to verify consumer idempotence,
/// inspect retried or locked messages, or clear inbox state to allow replaying messages during local development.
/// </remarks>
internal static class InboxCommand
{
    /// <summary>Creates the command with its subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>inbox</c> command.</returns>
    public static Command Create(CommonOptions common) =>
        new("inbox", "Inspect and manage MassTransit consumer inbox state and message idempotency.")
        {
            Status(common),
            List(common),
            Clean(common),
        };

    private static Command Status(CommonOptions common)
    {
        var service = new Argument<string?>("service")
        {
            Description = "Target service (knowledge, sleepdiary) or omitted for all services.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var k8s = new Option<bool>("--k8s") { Description = "Target Kubernetes cluster (auto-detected if cluster is running)." };

        var command = new Command("status", "Inspect total processed messages, retries, and active locks in InboxState.")
        {
            service,
            k8s,
        };

        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var dbEnv = new DatabaseEnvironment(root);
            var preferK8s = parseResult.GetValue(k8s);
            var rows = dbEnv.InboxSummary(preferK8s);

            var filter = parseResult.GetValue(service);
            if (!string.IsNullOrWhiteSpace(filter))
            {
                rows = rows.Where(r => r.Service.Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                if (rows.Count == 0)
                {
                    output.Error($"Unknown service '{filter}'. Supported services: knowledge, sleepdiary.");
                    return ExitCodes.NotFound;
                }
            }

            if (output.IsJson)
            {
                output.WriteJson(new
                {
                    ok = rows.All(r => r.Locked == 0),
                    inbox = rows.Select(r => new
                    {
                        service = r.Service,
                        totalProcessed = r.Total,
                        retried = r.Retried,
                        activeLocks = r.Locked,
                    }),
                });
                return ExitCodes.Success;
            }

            output.Line("=== MassTransit SQL Inbox Status ===");
            if (rows.Count == 0)
            {
                output.Line("No inbox records or database unreachable.");
            }
            else
            {
                output.Table(
                    ["Service", "Processed Messages", "Retried (>1)", "Active Locks", "Health"],
                    rows.Select(r => (IReadOnlyList<string>)[
                        r.Service,
                        r.Total.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        r.Retried.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        r.Locked.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        r.Locked > 0 ? "LOCKED" : "OK"
                    ]));
            }

            return ExitCodes.Success;
        });

        return command;
    }

    private static Command List(CommonOptions common)
    {
        var service = new Argument<string>("service")
        {
            Description = "Target service (knowledge, sleepdiary).",
        };
        var limit = new Option<int>("--limit", "-n") { Description = "Maximum number of rows to return (default 20)." };
        var k8s = new Option<bool>("--k8s") { Description = "Target Kubernetes cluster (auto-detected if cluster is running)." };

        var command = new Command("list", "List recent messages recorded in the service's InboxState table.")
        {
            service,
            limit,
            k8s,
        };

        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var targetService = parseResult.GetValue(service);
            if (string.IsNullOrWhiteSpace(targetService) ||
                (!targetService.Equals("knowledge", StringComparison.OrdinalIgnoreCase) &&
                 !targetService.Equals("sleepdiary", StringComparison.OrdinalIgnoreCase)))
            {
                output.Error($"Unknown service '{targetService}'. Supported services: knowledge, sleepdiary.");
                return ExitCodes.NotFound;
            }

            var max = parseResult.GetValue(limit);
            if (max <= 0)
            {
                max = 20;
            }

            var dbEnv = new DatabaseEnvironment(root);
            var messages = dbEnv.InboxMessages(targetService, max, parseResult.GetValue(k8s));

            if (output.IsJson)
            {
                output.WriteJson(messages.Select(m => new
                {
                    messageId = m.MessageId,
                    consumerId = m.ConsumerId,
                    received = m.Received,
                    receiveCount = m.ReceiveCount,
                    consumed = m.Consumed,
                }));
                return ExitCodes.Success;
            }

            output.Line($"=== Recent InboxState Messages for {targetService} ===");
            if (messages.Count == 0)
            {
                output.Line("No messages found in InboxState.");
            }
            else
            {
                output.Table(
                    ["Message ID", "Consumer ID", "Received", "Retries", "Consumed"],
                    messages.Select(m => (IReadOnlyList<string>)[
                        m.MessageId,
                        m.ConsumerId,
                        m.Received,
                        m.ReceiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        m.Consumed ?? "-"
                    ]));
            }

            return ExitCodes.Success;
        });

        return command;
    }

    private static Command Clean(CommonOptions common)
    {
        var service = new Argument<string?>("service")
        {
            Description = "Target service (knowledge, sleepdiary) or omitted for all services.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var k8s = new Option<bool>("--k8s") { Description = "Target Kubernetes cluster (auto-detected if cluster is running)." };

        var command = new Command("clean", "Clear InboxState records to allow consumers to re-process incoming messages during testing.")
        {
            service,
            k8s,
        };

        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var dbEnv = new DatabaseEnvironment(root);
            output.Line("Cleaning InboxState records...");
            var exit = dbEnv.CleanInbox(parseResult.GetValue(service), parseResult.GetValue(k8s), output);
            if (exit == ExitCodes.Success)
            {
                output.Line("Inbox state cleaned. Consumers can now re-process messages.");
            }

            return exit;
        });

        return command;
    }
}
