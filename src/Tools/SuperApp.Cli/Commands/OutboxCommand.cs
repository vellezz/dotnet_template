using System.CommandLine;
using SuperApp.Cli.LocalEnvironment;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp outbox status</c>: inspects MassTransit transactional outbox and RabbitMQ queue lag (ADR-0021, ADR-0035).</summary>
/// <remarks>
/// Queries the transactional OutboxMessage, OutboxState and InboxState tables across service schemas in MSSQL,
/// and queries RabbitMQ queue depths and error queues via rabbitmqctl.
/// </remarks>
internal static class OutboxCommand
{
    /// <summary>Creates the command with its subcommands.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>outbox</c> command.</returns>
    public static Command Create(CommonOptions common) =>
        new("outbox", "Inspect MassTransit transactional outbox tables and RabbitMQ queues.")
        {
            Status(common),
        };

    private static Command Status(CommonOptions common)
    {
        var k8s = new Option<bool>("--k8s") { Description = "Target Kubernetes cluster (auto-detected if cluster is running)." };
        var watch = new Option<bool>("--watch") { Description = "Watch outbox and queues continuously until Ctrl+C." };

        var command = new Command("status", "Inspect pending outbox messages, inbox processed records, and RabbitMQ queue depths.")
        {
            k8s,
            watch,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var dbEnv = new DatabaseEnvironment(root);
            var preferK8s = parseResult.GetValue(k8s);
            var isWatch = parseResult.GetValue(watch);

            do
            {
                var outboxRows = dbEnv.OutboxSummary(preferK8s);
                var queues = dbEnv.RabbitMqQueues(preferK8s);

                if (output.IsJson)
                {
                    output.WriteJson(new
                    {
                        ok = outboxRows.All(r => r.OutboxPending == 0) && queues.All(q => !q.IsError || q.Messages == 0),
                        outbox = outboxRows.Select(r => new
                        {
                            service = r.Service,
                            pending = r.OutboxPending,
                            inbox = r.InboxProcessed,
                            locks = r.ActiveLocks,
                        }),
                        queues = queues.Select(q => new
                        {
                            queue = q.Queue,
                            messages = q.Messages,
                            ready = q.Ready,
                            unacknowledged = q.Unacknowledged,
                            isError = q.IsError,
                        }),
                    });

                    if (!isWatch)
                    {
                        return ExitCodes.Success;
                    }
                }
                else
                {
                    if (isWatch)
                    {
                        Console.Clear();
                    }

                    output.Line("=== MassTransit SQL Outbox Status ===");
                    if (outboxRows.Count == 0)
                    {
                        output.Line("No outbox records or database unreachable.");
                    }
                    else
                    {
                        output.Table(
                            ["Service", "Pending Outbox", "Processed Inbox", "Active Locks", "Status"],
                            outboxRows.Select(r => (IReadOnlyList<string>)[
                                r.Service,
                                r.OutboxPending.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                r.InboxProcessed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                r.ActiveLocks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                r.OutboxPending > 0 ? "LAGGING" : "OK"
                            ]));
                    }

                    output.Line();
                    output.Line("=== RabbitMQ Queues ===");
                    if (queues.Count == 0)
                    {
                        output.Line("No queues found or RabbitMQ unreachable.");
                    }
                    else
                    {
                        output.Table(
                            ["Queue", "Total", "Ready", "Unacked", "Health"],
                            queues.Select(q => (IReadOnlyList<string>)[
                                q.Queue,
                                q.Messages.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                q.Ready.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                q.Unacknowledged.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                q.IsError ? (q.Messages > 0 ? "ERROR QUEUE (HAS MSGS)" : "ERROR QUEUE (EMPTY)") : (q.Messages > 0 ? "PROCESSING" : "IDLE")
                            ]));
                    }
                }

                if (isWatch)
                {
                    await Task.Delay(2000, cancellationToken);
                }
            }
            while (isWatch && !cancellationToken.IsCancellationRequested);

            return ExitCodes.Success;
        });

        return command;
    }
}
