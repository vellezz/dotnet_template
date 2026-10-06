using System.CommandLine;
using System.Text.Json;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Tests;

/// <summary>The commands run end to end on the real repository: exit codes, JSON output and errors for unknown names.</summary>
public sealed class CommandTests
{
    private static readonly string Root = RepositoryRoot.Find(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Tests must run inside the repository.");

    [Fact]
    public async Task List_services_as_json_describes_every_service()
    {
        var (exitCode, output, _) = await Run("list", "services", "--json");

        Assert.Equal(ExitCodes.Success, exitCode);
        using var document = JsonDocument.Parse(output);
        var names = document.RootElement.EnumerateArray().Select(service => service.GetProperty("name").GetString()).ToList();
        Assert.Contains("Knowledge", names);
        Assert.Contains("SleepDiary", names);
    }

    [Fact]
    public async Task Info_of_a_bff_lists_its_clients()
    {
        var (exitCode, output, _) = await Run("info", "Example.Bff", "--json");

        Assert.Equal(ExitCodes.Success, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("example", document.RootElement.GetProperty("experience").GetString());
        Assert.Contains("Knowledge", document.RootElement.GetProperty("clients").EnumerateArray().Select(client => client.GetString()));
    }

    [Fact]
    public async Task Info_of_an_unknown_name_exits_with_not_found()
    {
        var (exitCode, _, error) = await Run("info", "NoSuchService");

        Assert.Equal(ExitCodes.NotFound, exitCode);
        Assert.Contains("NoSuchService", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_doctor_rule_is_an_invalid_argument()
    {
        var (exitCode, _, _) = await Run("doctor", "--rule", "no-such-rule");

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
    }

    [Fact]
    public async Task Call_with_unknown_component_exits_with_not_found()
    {
        var (exitCode, _, error) = await Run("call", "no-such-component", "/api/test");

        Assert.Equal(ExitCodes.NotFound, exitCode);
        Assert.Contains("no-such-component", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Db_query_with_empty_sql_exits_with_invalid_arguments()
    {
        var (exitCode, _, error) = await Run("db", "query", "   ");

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Outbox_status_as_json_returns_valid_structure()
    {
        var (exitCode, output, _) = await Run("outbox", "status", "--json");

        Assert.Equal(ExitCodes.Success, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.TryGetProperty("outbox", out var outbox));
        Assert.True(document.RootElement.TryGetProperty("queues", out var queues));
        Assert.Equal(JsonValueKind.Array, outbox.ValueKind);
        Assert.Equal(JsonValueKind.Array, queues.ValueKind);
    }

    [Fact]
    public async Task Inbox_status_as_json_returns_valid_structure()
    {
        var (exitCode, output, _) = await Run("inbox", "status", "--json");

        Assert.Equal(ExitCodes.Success, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.TryGetProperty("inbox", out var inbox));
        Assert.Equal(JsonValueKind.Array, inbox.ValueKind);
    }

    [Fact]
    public async Task Inbox_list_with_unknown_service_exits_with_not_found()
    {
        var (exitCode, _, error) = await Run("inbox", "list", "no-such-service");

        Assert.Equal(ExitCodes.NotFound, exitCode);
        Assert.Contains("no-such-service", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inbox_clean_with_unknown_service_exits_with_not_found()
    {
        var (exitCode, _, error) = await Run("inbox", "clean", "no-such-service");

        Assert.Equal(ExitCodes.NotFound, exitCode);
        Assert.Contains("no-such-service", error, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CliApplication.Create()
            .Parse([.. args, "--root", Root])
            .InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }
}
