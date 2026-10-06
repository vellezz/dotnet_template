using System.Diagnostics;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>Interacts with MSSQL and RabbitMQ across local Kubernetes and Docker Compose environments (ADR-0021, ADR-0034).</summary>
/// <remarks>
/// Executes SQL commands via <c>sqlcmd</c> inside the running database container/pod and queries queue depths
/// using <c>rabbitmqctl</c> to provide diagnostics and message inspection without requiring external database drivers.
/// </remarks>
/// <param name="root">Repository root path.</param>
/// <param name="model">Optional pre-scanned repository model.</param>
internal sealed class DatabaseEnvironment(string root, RepositoryModel? model = null)
{
    private const string SaPassword = "Dev!Passw0rd1";
    private const string DatabaseName = "SuperApp";
    private readonly RepositoryModel _model = model ?? RepositoryScanner.Scan(root);

    /// <summary>Checks whether the given service name is known in the repository.</summary>
    /// <param name="service">Service name or key.</param>
    /// <returns><see langword="true"/> if known.</returns>
    public bool IsKnownService(string service) =>
        _model.Services.Any(s => string.Equals(s.Key, service, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(s.Name, service, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns a comma-separated list of known service keys.</summary>
    /// <returns>Known service keys.</returns>
    public string KnownServicesList() =>
        string.Join(", ", _model.Services.Select(s => s.Key));

    /// <summary>Normalizes service name or key to its canonical lower kebab-case key.</summary>
    /// <param name="service">Service name or key.</param>
    /// <returns>Canonical key, or null if not found.</returns>
    public string? NormalizeServiceName(string service) =>
        _model.Services.FirstOrDefault(s => string.Equals(s.Key, service, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(s.Name, service, StringComparison.OrdinalIgnoreCase))?.Key;

    /// <summary>Queries summary of transactional Outbox and Inbox tables for all schemas in MSSQL.</summary>
    /// <param name="preferK8s">Whether to target Kubernetes or Docker Compose.</param>
    /// <returns>Rows containing outbox counts.</returns>
    public IReadOnlyList<(string Service, long OutboxPending, long InboxProcessed, long ActiveLocks)> OutboxSummary(bool preferK8s)
    {
        const string query = @"
SET NOCOUNT ON;
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql + IIF(@sql = N'', N'', N' UNION ALL ') +
    N'SELECT ''' + s.name + N''' AS Service, ' +
    N'(SELECT COUNT(*) FROM ' + QUOTENAME(s.name) + N'.OutboxMessage) AS OutboxPending, ' +
    N'(SELECT COUNT(*) FROM ' + QUOTENAME(s.name) + N'.InboxState) AS InboxProcessed, ' +
    N'(SELECT COUNT(*) FROM ' + QUOTENAME(s.name) + N'.OutboxState WHERE LockId IS NOT NULL) AS ActiveLocks '
FROM sys.schemas s
JOIN sys.tables t ON s.schema_id = t.schema_id
WHERE t.name = 'OutboxMessage';
IF @sql <> N'' EXEC sp_executesql @sql;
";
        var resultText = QuerySql(query, preferK8s);
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return [];
        }

        var lines = resultText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = new List<(string Service, long OutboxPending, long InboxProcessed, long ActiveLocks)>();

        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 &&
                long.TryParse(parts[1], out var outbox) &&
                long.TryParse(parts[2], out var inbox) &&
                long.TryParse(parts[3], out var locks))
            {
                rows.Add((parts[0], outbox, inbox, locks));
            }
        }

        return rows;
    }

    /// <summary>Queries summary of transactional InboxState tables for all schemas in MSSQL.</summary>
    /// <param name="preferK8s">Whether to target Kubernetes or Docker Compose.</param>
    /// <returns>Rows containing inbox counts.</returns>
    public IReadOnlyList<(string Service, long Total, long Retried, long Locked)> InboxSummary(bool preferK8s)
    {
        const string query = @"
SET NOCOUNT ON;
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql + IIF(@sql = N'', N'', N' UNION ALL ') +
    N'SELECT ''' + s.name + N''' AS Service, ' +
    N'COUNT(*) AS Total, ' +
    N'COALESCE(SUM(CASE WHEN ReceiveCount > 1 THEN 1 ELSE 0 END), 0) AS Retried, ' +
    N'COALESCE(SUM(CASE WHEN LockId != ''00000000-0000-0000-0000-000000000000'' THEN 1 ELSE 0 END), 0) AS Locked ' +
    N'FROM ' + QUOTENAME(s.name) + N'.InboxState '
FROM sys.schemas s
JOIN sys.tables t ON s.schema_id = t.schema_id
WHERE t.name = 'InboxState';
IF @sql <> N'' EXEC sp_executesql @sql;
";
        var resultText = QuerySql(query, preferK8s);
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return [];
        }

        var lines = resultText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = new List<(string Service, long Total, long Retried, long Locked)>();

        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 &&
                long.TryParse(parts[1], out var total) &&
                long.TryParse(parts[2], out var retried) &&
                long.TryParse(parts[3], out var locked))
            {
                rows.Add((parts[0], total, retried, locked));
            }
        }

        return rows;
    }

    /// <summary>Lists recent messages stored in any service's InboxState table.</summary>
    /// <param name="service">Service name or key.</param>
    /// <param name="limit">Max rows to retrieve.</param>
    /// <param name="preferK8s">Whether to prefer Kubernetes.</param>
    /// <returns>List of message rows.</returns>
    public IReadOnlyList<(string MessageId, string ConsumerId, string Received, int ReceiveCount, string? Consumed)> InboxMessages(string service, int limit, bool preferK8s)
    {
        var schema = NormalizeServiceName(service) ?? service.ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^[a-zA-Z0-9_]+$"))
        {
            return [];
        }

        var query = $@"
SET NOCOUNT ON;
IF EXISTS (SELECT 1 FROM sys.schemas s JOIN sys.tables t ON s.schema_id = t.schema_id WHERE s.name = '{schema}' AND t.name = 'InboxState')
BEGIN
    SELECT TOP ({limit})
           CONVERT(nvarchar(36), MessageId),
           CONVERT(nvarchar(36), ConsumerId),
           CONVERT(nvarchar(23), Received, 126),
           ReceiveCount,
           COALESCE(CONVERT(nvarchar(23), Consumed, 126), '-')
    FROM [{schema}].InboxState
    ORDER BY Id DESC;
END
";
        var resultText = QuerySql(query, preferK8s);
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return [];
        }

        var lines = resultText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = new List<(string MessageId, string ConsumerId, string Received, int ReceiveCount, string? Consumed)>();

        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5 &&
                Guid.TryParse(parts[0], out _) &&
                int.TryParse(parts[3], out var count))
            {
                rows.Add((parts[0], parts[1], parts[2], count, parts[4]));
            }
        }

        return rows;
    }

    /// <summary>Cleans InboxState and related inbox outbox messages for replay testing.</summary>
    /// <param name="service">Target service (or null for all schemas with InboxState).</param>
    /// <param name="preferK8s">Whether to prefer Kubernetes.</param>
    /// <param name="output">Output writer.</param>
    /// <returns>Exit code.</returns>
    public int CleanInbox(string? service, bool preferK8s, OutputWriter output)
    {
        var target = !string.IsNullOrEmpty(service) ? (NormalizeServiceName(service) ?? service.ToLowerInvariant()) : null;
        if (target is not null && !IsKnownService(target))
        {
            output.Error($"Unknown service '{service}'. Known services: {KnownServicesList()}.");
            return ExitCodes.NotFound;
        }

        var filter = target is not null ? $"AND s.name = '{target}'" : "";
        var query = $@"
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql +
    N'DELETE FROM ' + QUOTENAME(s.name) + N'.OutboxMessage WHERE InboxMessageId IS NOT NULL; ' +
    N'DELETE FROM ' + QUOTENAME(s.name) + N'.InboxState; '
FROM sys.schemas s
JOIN sys.tables t ON s.schema_id = t.schema_id
WHERE t.name = 'InboxState' {filter};
IF @sql <> N'' EXEC sp_executesql @sql;
";
        return ExecuteSql(query, preferK8s, output);
    }

    /// <summary>Queries active RabbitMQ queues and their message counts.</summary>
    /// <param name="preferK8s">Whether to target Kubernetes or Docker Compose.</param>
    /// <returns>List of queue stats.</returns>
    public IReadOnlyList<(string Queue, long Messages, long Ready, long Unacknowledged, bool IsError)> RabbitMqQueues(bool preferK8s)
    {
        var output = RunProcess(preferK8s, "rabbitmq",
            k8sArgs: ["exec", "deploy/rabbitmq", "--", "rabbitmqctl", "list_queues", "name", "messages", "messages_ready", "messages_unacknowledged"],
            dockerArgs: ["compose", "-f", ComposeFile.Path, "exec", "-T", "rabbitmq", "rabbitmqctl", "list_queues", "name", "messages", "messages_ready", "messages_unacknowledged"]);

        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var queues = new List<(string Queue, long Messages, long Ready, long Unacknowledged, bool IsError)>();

        foreach (var line in lines)
        {
            if (line.StartsWith("Timeout:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Listing", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("name\t", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("name ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 &&
                long.TryParse(parts[1], out var messages) &&
                long.TryParse(parts[2], out var ready) &&
                long.TryParse(parts[3], out var unacked))
            {
                var queueName = parts[0];
                var isError = queueName.EndsWith("_error", StringComparison.OrdinalIgnoreCase) ||
                              queueName.EndsWith("_skipped", StringComparison.OrdinalIgnoreCase);
                queues.Add((queueName, messages, ready, unacked, isError));
            }
        }

        return queues.OrderBy(q => q.Queue, StringComparer.Ordinal).ToList();
    }

    /// <summary>Executes arbitrary SQL against the SuperApp database.</summary>
    /// <param name="sql">SQL script.</param>
    /// <param name="preferK8s">Whether to prefer Kubernetes.</param>
    /// <param name="output">Output writer.</param>
    /// <returns>Exit code.</returns>
    public int ExecuteSql(string sql, bool preferK8s, OutputWriter output)
    {
        try
        {
            var res = RunProcess(preferK8s, "mssql",
                k8sArgs: ["exec", "deploy/mssql", "--", "/opt/mssql-tools18/bin/sqlcmd", "-C", "-b", "-S", "localhost", "-U", "sa", "-P", SaPassword, "-d", DatabaseName, "-Q", sql],
                dockerArgs: ["compose", "-f", ComposeFile.Path, "exec", "-T", "mssql", "/opt/mssql-tools18/bin/sqlcmd", "-C", "-b", "-S", "localhost", "-U", "sa", "-P", SaPassword, "-d", DatabaseName, "-Q", sql]);

            if (res.Contains("Msg ", StringComparison.Ordinal) && res.Contains("Level ", StringComparison.Ordinal))
            {
                output.Error(res);
                return ExitCodes.Failed;
            }

            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            output.Error($"SQL execution failed: {ex.Message}");
            return ExitCodes.Failed;
        }
    }

    /// <summary>Queries arbitrary SQL against the SuperApp database, returning text output.</summary>
    /// <param name="sql">SQL query.</param>
    /// <param name="preferK8s">Whether to prefer Kubernetes.</param>
    /// <returns>Result output text.</returns>
    public string QuerySql(string sql, bool preferK8s)
    {
        try
        {
            return RunProcess(preferK8s, "mssql",
                k8sArgs: ["exec", "deploy/mssql", "--", "/opt/mssql-tools18/bin/sqlcmd", "-C", "-b", "-S", "localhost", "-U", "sa", "-P", SaPassword, "-d", DatabaseName, "-Q", sql],
                dockerArgs: ["compose", "-f", ComposeFile.Path, "exec", "-T", "mssql", "/opt/mssql-tools18/bin/sqlcmd", "-C", "-b", "-S", "localhost", "-U", "sa", "-P", SaPassword, "-d", DatabaseName, "-Q", sql]);
        }
        catch
        {
            return string.Empty;
        }
    }

    private string RunProcess(bool preferK8s, string targetComponent, IReadOnlyList<string> k8sArgs, IReadOnlyList<string> dockerArgs)
    {
        var useK8s = preferK8s || IsKubernetesAvailable();
        var executable = useK8s ? "kubectl" : "docker";
        var args = useK8s ? k8sArgs : dockerArgs;

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException($"Command '{executable} {string.Join(" ", args)}' exited with {process.ExitCode}: {error}");
        }

        return output;
    }

    private bool IsKubernetesAvailable()
    {
        try
        {
            var start = new ProcessStartInfo("kubectl")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ArgumentList = { "get", "deploy", "mssql" },
            };
            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
