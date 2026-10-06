using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>Interacts with MSSQL via ADO.NET and RabbitMQ across local development environments (ADR-0021, ADR-0034).</summary>
/// <remarks>
/// Queries transactional Outbox and Inbox tables using typed <see cref="SqlConnection"/> without requiring external
/// process shells or sqlcmd text parsing.
/// </remarks>
/// <param name="root">Repository root path.</param>
/// <param name="model">Optional pre-scanned repository model.</param>
internal sealed class DatabaseEnvironment(string root, RepositoryModel? model = null)
{
    private const string DefaultConnectionString =
        "Server=localhost,1433;Database=SuperApp;User Id=sa;Password=Dev!Passw0rd1;TrustServerCertificate=True;Connect Timeout=5;";

    private readonly RepositoryModel _model = model ?? RepositoryScanner.Scan(root);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__SuperApp") ??
        Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING") ??
        DefaultConnectionString;

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
    /// <param name="preferK8s">Ignored when using direct ADO.NET connection.</param>
    /// <returns>Rows containing outbox counts.</returns>
    public IReadOnlyList<(string Service, long OutboxPending, long InboxProcessed, long ActiveLocks)> OutboxSummary(bool preferK8s = false)
    {
        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            var schemas = GetSchemasWithTable(connection, "OutboxMessage");
            var results = new List<(string Service, long OutboxPending, long InboxProcessed, long ActiveLocks)>();

            foreach (var schema in schemas)
            {
                var query = $@"
                    SELECT (SELECT COUNT_BIG(*) FROM [{schema}].OutboxMessage) AS OutboxPending,
                           (SELECT COUNT_BIG(*) FROM [{schema}].InboxState) AS InboxProcessed,
                           (SELECT COUNT_BIG(*) FROM [{schema}].OutboxState WHERE LockId IS NOT NULL) AS ActiveLocks;";

                using var cmd = new SqlCommand(query, connection);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    results.Add((
                        schema,
                        reader.GetInt64(0),
                        reader.GetInt64(1),
                        reader.GetInt64(2)));
                }
            }

            return results;
        }
        catch (SqlException)
        {
            return [];
        }
    }

    /// <summary>Queries summary of transactional InboxState tables for all schemas in MSSQL.</summary>
    /// <param name="preferK8s">Ignored when using direct ADO.NET connection.</param>
    /// <returns>Rows containing inbox counts.</returns>
    public IReadOnlyList<(string Service, long Total, long Retried, long Locked)> InboxSummary(bool preferK8s = false)
    {
        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            var schemas = GetSchemasWithTable(connection, "InboxState");
            var results = new List<(string Service, long Total, long Retried, long Locked)>();

            foreach (var schema in schemas)
            {
                var query = $@"
                    SELECT COUNT_BIG(*) AS Total,
                           COALESCE(SUM(CASE WHEN ReceiveCount > 1 THEN 1 ELSE 0 END), 0) AS Retried,
                           COALESCE(SUM(CASE WHEN LockId != '00000000-0000-0000-0000-000000000000' THEN 1 ELSE 0 END), 0) AS Locked
                    FROM [{schema}].InboxState;";

                using var cmd = new SqlCommand(query, connection);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    results.Add((
                        schema,
                        reader.GetInt64(0),
                        Convert.ToInt64(reader.GetValue(1)),
                        Convert.ToInt64(reader.GetValue(2))));
                }
            }

            return results;
        }
        catch (SqlException)
        {
            return [];
        }
    }

    /// <summary>Lists recent messages stored in any service's InboxState table.</summary>
    /// <param name="service">Service name or key.</param>
    /// <param name="limit">Max rows to retrieve.</param>
    /// <param name="preferK8s">Ignored when using direct ADO.NET connection.</param>
    /// <returns>List of message rows.</returns>
    public IReadOnlyList<(string MessageId, string ConsumerId, string Received, int ReceiveCount, string? Consumed)> InboxMessages(string service, int limit, bool preferK8s = false)
    {
        var schema = NormalizeServiceName(service) ?? service.ToLowerInvariant();
        if (!Regex.IsMatch(schema, "^[a-zA-Z0-9_]+$"))
        {
            return [];
        }

        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            var query = $@"
                SELECT TOP (@Limit)
                       MessageId,
                       ConsumerId,
                       Received,
                       ReceiveCount,
                       Consumed
                FROM [{schema}].InboxState
                ORDER BY Id DESC;";

            using var cmd = new SqlCommand(query, connection);
            cmd.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;

            using var reader = cmd.ExecuteReader();
            var rows = new List<(string MessageId, string ConsumerId, string Received, int ReceiveCount, string? Consumed)>();

            while (reader.Read())
            {
                var messageId = reader.GetGuid(0).ToString();
                var consumerId = reader.GetGuid(1).ToString();
                var received = reader.GetDateTime(2).ToString("yyyy-MM-ddTHH:mm:ss.fff");
                var receiveCount = reader.GetInt32(3);
                var consumed = reader.IsDBNull(4) ? null : reader.GetDateTime(4).ToString("yyyy-MM-ddTHH:mm:ss.fff");

                rows.Add((messageId, consumerId, received, receiveCount, consumed));
            }

            return rows;
        }
        catch (SqlException)
        {
            return [];
        }
    }

    /// <summary>Cleans InboxState and related inbox outbox messages for replay testing.</summary>
    /// <param name="service">Target service (or null for all schemas with InboxState).</param>
    /// <param name="preferK8s">Ignored when using direct ADO.NET connection.</param>
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

        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            var schemas = target is not null
                ? [target]
                : GetSchemasWithTable(connection, "InboxState");

            foreach (var schema in schemas)
            {
                var sql = $@"
                    SET QUOTED_IDENTIFIER ON;
                    SET ANSI_NULLS ON;
                    DELETE FROM [{schema}].OutboxMessage WHERE InboxMessageId IS NOT NULL;
                    DELETE FROM [{schema}].InboxState;";

                using var cmd = new SqlCommand(sql, connection);
                cmd.ExecuteNonQuery();
            }

            return ExitCodes.Success;
        }
        catch (SqlException ex)
        {
            output.Error($"Database operation failed: {ex.Message}");
            return ExitCodes.Failed;
        }
    }

    /// <summary>Executes arbitrary SQL query and returns formatted tabular output.</summary>
    /// <param name="sql">SQL query string.</param>
    /// <param name="preferK8s">Ignored when using direct ADO.NET connection.</param>
    /// <returns>Formatted text output.</returns>
    public string QuerySql(string sql, bool preferK8s = false)
    {
        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqlCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            var sb = new StringBuilder();
            var colCount = reader.FieldCount;

            if (colCount == 0)
            {
                return $"({reader.RecordsAffected} rows affected)";
            }

            var colNames = new string[colCount];
            var colLengths = new int[colCount];

            for (var i = 0; i < colCount; i++)
            {
                colNames[i] = reader.GetName(i);
                colLengths[i] = Math.Max(colNames[i].Length, 10);
            }

            var rows = new List<string[]>();
            while (reader.Read())
            {
                var row = new string[colCount];
                for (var i = 0; i < colCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "";
                    colLengths[i] = Math.Max(colLengths[i], row[i].Length);
                }
                rows.Add(row);
            }

            // Header
            for (var i = 0; i < colCount; i++)
            {
                sb.Append(colNames[i].PadRight(colLengths[i] + 2));
            }
            sb.AppendLine();

            // Separator
            for (var i = 0; i < colCount; i++)
            {
                sb.Append(new string('-', colLengths[i]).PadRight(colLengths[i] + 2));
            }
            sb.AppendLine();

            // Rows
            foreach (var row in rows)
            {
                for (var i = 0; i < colCount; i++)
                {
                    sb.Append(row[i].PadRight(colLengths[i] + 2));
                }
                sb.AppendLine();
            }

            sb.AppendLine($"\n({rows.Count} rows affected)");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"SQL error: {ex.Message}";
        }
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

    private static List<string> GetSchemasWithTable(SqlConnection connection, string tableName)
    {
        const string query = @"
            SELECT s.name
            FROM sys.schemas s
            JOIN sys.tables t ON s.schema_id = t.schema_id
            WHERE t.name = @TableName
            ORDER BY s.name;";

        using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.Add("@TableName", SqlDbType.NVarChar).Value = tableName;

        using var reader = cmd.ExecuteReader();
        var schemas = new List<string>();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (Regex.IsMatch(name, "^[a-zA-Z0-9_]+$"))
            {
                schemas.Add(name);
            }
        }
        return schemas;
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
