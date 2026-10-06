using System.Diagnostics;
using System.Text;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>Interacts with MSSQL and RabbitMQ across local Kubernetes and Docker Compose environments (ADR-0021, ADR-0034).</summary>
/// <remarks>
/// Executes SQL commands via <c>sqlcmd</c> inside the running database container/pod and queries queue depths
/// using <c>rabbitmqctl</c> to provide diagnostics and test data seeding without requiring external database drivers.
/// </remarks>
/// <param name="root">Repository root path.</param>
internal sealed class DatabaseEnvironment(string root)
{
    private const string SaPassword = "Dev!Passw0rd1";
    private const string DatabaseName = "SuperApp";

    /// <summary>Seeds realistic development data into the services.</summary>
    /// <param name="service">Target service (Knowledge, SleepDiary, or null for all).</param>
    /// <param name="clean">Whether to wipe existing data before seeding.</param>
    /// <param name="preferK8s">Whether to prefer Kubernetes over Docker Compose.</param>
    /// <param name="output">Output writer.</param>
    /// <returns>Exit code.</returns>
    public int Seed(string? service, bool clean, bool preferK8s, OutputWriter output)
    {
        var target = service?.ToLowerInvariant();
        var seedKnowledge = string.IsNullOrEmpty(target) || target == "knowledge";
        var seedSleepDiary = string.IsNullOrEmpty(target) || target == "sleepdiary";

        if (!seedKnowledge && !seedSleepDiary)
        {
            output.Error($"Unknown service '{service}'. Supported services: knowledge, sleepdiary.");
            return ExitCodes.NotFound;
        }

        if (clean)
        {
            output.Line("Cleaning existing data before seeding...");
            Clean(service, preferK8s, output);
        }

        if (seedKnowledge)
        {
            output.Line("Seeding Knowledge service (categories, published articles, favorites)...");
            var sqlKnowledge = BuildKnowledgeSeedSql();
            var exit = ExecuteSql(sqlKnowledge, preferK8s, output);
            if (exit != ExitCodes.Success)
            {
                output.Error("Failed to seed Knowledge database.");
                return exit;
            }
        }

        if (seedSleepDiary)
        {
            output.Line("Seeding SleepDiary service (7 days of sleep entries for test user)...");
            var sqlSleepDiary = BuildSleepDiarySeedSql();
            var exit = ExecuteSql(sqlSleepDiary, preferK8s, output);
            if (exit != ExitCodes.Success)
            {
                output.Error("Failed to seed SleepDiary database.");
                return exit;
            }
        }

        output.Line("Database seeding completed successfully.");
        return ExitCodes.Success;
    }

    /// <summary>Cleans development data from service tables.</summary>
    /// <param name="service">Target service (Knowledge, SleepDiary, or null for all).</param>
    /// <param name="preferK8s">Whether to prefer Kubernetes over Docker Compose.</param>
    /// <param name="output">Output writer.</param>
    /// <returns>Exit code.</returns>
    public int Clean(string? service, bool preferK8s, OutputWriter output)
    {
        var target = service?.ToLowerInvariant();
        var cleanKnowledge = string.IsNullOrEmpty(target) || target == "knowledge";
        var cleanSleepDiary = string.IsNullOrEmpty(target) || target == "sleepdiary";

        if (!cleanKnowledge && !cleanSleepDiary)
        {
            output.Error($"Unknown service '{service}'. Supported services: knowledge, sleepdiary.");
            return ExitCodes.NotFound;
        }

        var sb = new StringBuilder("SET NOCOUNT ON;\n");
        if (cleanKnowledge)
        {
            sb.AppendLine("DELETE FROM knowledge.Favorites;");
            sb.AppendLine("DELETE FROM knowledge.MaterialCompletions;");
            sb.AppendLine("DELETE FROM knowledge.MaterialCategories;");
            sb.AppendLine("DELETE FROM knowledge.ContentBlocks;");
            sb.AppendLine("DELETE FROM knowledge.CollectionCategories;");
            sb.AppendLine("DELETE FROM knowledge.CollectionItems;");
            sb.AppendLine("DELETE FROM knowledge.Collections;");
            sb.AppendLine("DELETE FROM knowledge.Materials;");
            sb.AppendLine("DELETE FROM knowledge.Categories;");
        }

        if (cleanSleepDiary)
        {
            sb.AppendLine("DELETE FROM sleepdiary.SleepEntries;");
        }

        return ExecuteSql(sb.ToString(), preferK8s, output);
    }

    /// <summary>Queries summary of transactional Outbox and Inbox tables for registered services.</summary>
    /// <param name="preferK8s">Whether to target Kubernetes or Docker Compose.</param>
    /// <returns>Rows containing outbox counts.</returns>
    public IReadOnlyList<(string Service, long OutboxPending, long InboxProcessed, long ActiveLocks)> OutboxSummary(bool preferK8s)
    {
        const string query = @"
SET NOCOUNT ON;
SELECT 'knowledge' AS Service,
       (SELECT COUNT(*) FROM knowledge.OutboxMessage) AS OutboxPending,
       (SELECT COUNT(*) FROM knowledge.InboxState) AS InboxProcessed,
       (SELECT COUNT(*) FROM knowledge.OutboxState WHERE LockId IS NOT NULL) AS ActiveLocks
UNION ALL
SELECT 'sleepdiary' AS Service,
       (SELECT COUNT(*) FROM sleepdiary.OutboxMessage) AS OutboxPending,
       (SELECT COUNT(*) FROM sleepdiary.InboxState) AS InboxProcessed,
       (SELECT COUNT(*) FROM sleepdiary.OutboxState WHERE LockId IS NOT NULL) AS ActiveLocks;
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
                (parts[0].Equals("knowledge", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("sleepdiary", StringComparison.OrdinalIgnoreCase)) &&
                long.TryParse(parts[1], out var outbox) &&
                long.TryParse(parts[2], out var inbox) &&
                long.TryParse(parts[3], out var locks))
            {
                rows.Add((parts[0], outbox, inbox, locks));
            }
        }

        return rows;
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

    private static string BuildKnowledgeSeedSql() =>
        @"
SET NOCOUNT ON;
BEGIN TRANSACTION;

DECLARE @catSleep UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM knowledge.Categories WHERE Slug = 'sleep-hygiene');
IF @catSleep IS NULL
BEGIN
    SET @catSleep = NEWID();
    INSERT INTO knowledge.Categories (Id, Name, Slug) VALUES (@catSleep, N'Sleep Hygiene', N'sleep-hygiene');
END

DECLARE @catNutrition UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM knowledge.Categories WHERE Slug = 'nutrition-and-rest');
IF @catNutrition IS NULL
BEGIN
    SET @catNutrition = NEWID();
    INSERT INTO knowledge.Categories (Id, Name, Slug) VALUES (@catNutrition, N'Nutrition & Rest', N'nutrition-and-rest');
END

DECLARE @catMental UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM knowledge.Categories WHERE Slug = 'mental-wellbeing');
IF @catMental IS NULL
BEGIN
    SET @catMental = NEWID();
    INSERT INTO knowledge.Categories (Id, Name, Slug) VALUES (@catMental, N'Mental Wellbeing', N'mental-wellbeing');
END

DECLARE @matCircadian UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM knowledge.Materials WHERE Title = N'Circadian Rhythms & Light Optimization');
IF @matCircadian IS NULL
BEGIN
    SET @matCircadian = NEWID();
    INSERT INTO knowledge.Materials (Id, Type, Title, Description, MainMediaUrl, MainMediaDurationSeconds, Status, ContentPlainText, ReadingTimeMinutes, CreatedAt, UpdatedAt, PublishedAt)
    VALUES (@matCircadian, N'Article', N'Circadian Rhythms & Light Optimization', N'How blue light and morning sunlight regulate your master biological clock.', N'https://example.com/circadian.jpg', NULL, N'Published', N'Natural sunlight exposure within 30 minutes of waking triggers cortisol awakening response and sets your internal master clock for the entire day. Avoiding blue light 2 hours before bed allows natural melatonin secretion.', 6, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

    INSERT INTO knowledge.MaterialCategories (MaterialId, CategoryId) VALUES (@matCircadian, @catSleep);
END

DECLARE @matMagnesium UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM knowledge.Materials WHERE Title = N'Magnesium & Sleep Architecture');
IF @matMagnesium IS NULL
BEGIN
    SET @matMagnesium = NEWID();
    INSERT INTO knowledge.Materials (Id, Type, Title, Description, MainMediaUrl, MainMediaDurationSeconds, Status, ContentPlainText, ReadingTimeMinutes, CreatedAt, UpdatedAt, PublishedAt)
    VALUES (@matMagnesium, N'Article', N'Magnesium & Sleep Architecture', N'The biochemical mechanism of magnesium glycinate on GABA receptors.', N'https://example.com/magnesium.jpg', NULL, N'Published', N'Magnesium acts as an agonist for GABA receptors, promoting parasympathetic nervous tone, reducing sleep latency and supporting deep delta wave sleep.', 4, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

    INSERT INTO knowledge.MaterialCategories (MaterialId, CategoryId) VALUES (@matMagnesium, @catNutrition);
END

DECLARE @matMindfulness UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM knowledge.Materials WHERE Title = N'Mindfulness & Sleep Latency');
IF @matMindfulness IS NULL
BEGIN
    SET @matMindfulness = NEWID();
    INSERT INTO knowledge.Materials (Id, Type, Title, Description, MainMediaUrl, MainMediaDurationSeconds, Status, ContentPlainText, ReadingTimeMinutes, CreatedAt, UpdatedAt, PublishedAt)
    VALUES (@matMindfulness, N'Article', N'Mindfulness & Sleep Latency', N'Scientific breathing techniques to reduce pre-sleep autonomic arousal.', N'https://example.com/mindfulness.jpg', NULL, N'Published', N'Physiological sighs and 4-7-8 breathing shift autonomic balance from sympathetic fight-or-flight to rest-and-digest, lowering heart rate and bedtime latency.', 5, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

    INSERT INTO knowledge.MaterialCategories (MaterialId, CategoryId) VALUES (@matMindfulness, @catMental);
END

DECLARE @editorUserId NVARCHAR(200) = N'c344d96e-1f0e-4212-885a-844a0c494a27';
IF NOT EXISTS (SELECT 1 FROM knowledge.Favorites WHERE UserId = @editorUserId AND ItemId = @matCircadian)
BEGIN
    INSERT INTO knowledge.Favorites (Id, UserId, ItemType, ItemId, AddedAt) VALUES (NEWID(), @editorUserId, N'Material', @matCircadian, SYSDATETIMEOFFSET());
END

COMMIT TRANSACTION;
";

    private static string BuildSleepDiarySeedSql() =>
        @"
SET NOCOUNT ON;
BEGIN TRANSACTION;

DECLARE @userId NVARCHAR(200) = N'c344d96e-1f0e-4212-885a-844a0c494a27';
DECLARE @today DATE = CAST(GETUTCDATE() AS DATE);
DECLARE @i INT = 0;

WHILE @i < 7
BEGIN
    DECLARE @entryDate DATE = DATEADD(DAY, -@i, @today);
    DECLARE @bedTime DATETIME2(0) = DATEADD(MINUTE, 23 * 60, CAST(@entryDate AS DATETIME2(0)));
    DECLARE @wakeTime DATETIME2(0) = DATEADD(MINUTE, 7 * 60 + 15 + (@i % 3) * 15, DATEADD(DAY, 1, CAST(@entryDate AS DATETIME2(0))));
    DECLARE @latency INT = 10 + (@i % 4) * 5;
    DECLARE @awakenings INT = @i % 2;
    DECLARE @quality INT = 4 + (@i % 2);
    DECLARE @timeInBed INT = DATEDIFF(MINUTE, @bedTime, @wakeTime);
    DECLARE @sleepMinutes INT = @timeInBed - @latency - (@awakenings * 10);

    IF EXISTS (SELECT 1 FROM sleepdiary.SleepEntries WHERE UserId = @userId AND Date = @entryDate)
    BEGIN
        UPDATE sleepdiary.SleepEntries
        SET BedTime = @bedTime,
            WakeTime = @wakeTime,
            SleepLatencyMinutes = @latency,
            Awakenings = @awakenings,
            Quality = @quality,
            TimeInBedMinutes = @timeInBed,
            SleepMinutes = @sleepMinutes,
            UpdatedAt = SYSDATETIMEOFFSET()
        WHERE UserId = @userId AND Date = @entryDate;
    END
    ELSE
    BEGIN
        INSERT INTO sleepdiary.SleepEntries (
            Id, UserId, Date, BedTime, WakeTime, SleepLatencyMinutes, Awakenings, Quality, Notes, TimeInBedMinutes, SleepMinutes, CreatedAt, UpdatedAt
        )
        VALUES (
            NEWID(),
            @userId,
            @entryDate,
            @bedTime,
            @wakeTime,
            @latency,
            @awakenings,
            @quality,
            CASE @i
                WHEN 0 THEN N'Woke up rested, energetic morning.'
                WHEN 1 THEN N'Deep restorative sleep after evening workout.'
                WHEN 2 THEN N'Quick sleep latency, quiet night.'
                ELSE N'Solid continuous rest.'
            END,
            @timeInBed,
            @sleepMinutes,
            SYSDATETIMEOFFSET(),
            SYSDATETIMEOFFSET()
        );
    END

    SET @i = @i + 1;
END;

COMMIT TRANSACTION;
";
}
