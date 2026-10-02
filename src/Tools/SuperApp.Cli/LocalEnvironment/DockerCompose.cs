using System.Diagnostics;
using System.Text.Json;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>Runs <c>docker compose</c> on <c>deploy/local/docker-compose.yml</c> and reads the state of its containers (ADR-0034).</summary>
/// <remarks>
/// The commands are the ones documented in chapter 13 of the developer guide; the tool only saves typing the file and profile. Long
/// commands (<c>up --build</c>) write their progress straight to the console.
/// </remarks>
/// <param name="root">Full path of the repository root.</param>
internal sealed class DockerCompose(string root)
{
    /// <summary>Runs <c>docker compose -f deploy/local/docker-compose.yml</c> with the given arguments, showing its output.</summary>
    /// <param name="arguments">Arguments after the file, e.g. <c>--profile app up -d</c>.</param>
    /// <returns>The exit code of docker.</returns>
    /// <exception cref="InvalidOperationException">Docker is not installed or not on the path.</exception>
    public int Run(params string[] arguments)
    {
        using var process = Start(redirect: false, arguments);
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>Lists the containers of the project (also stopped ones) with their state and health.</summary>
    /// <returns>Service name, state (<c>running</c>, <c>exited</c>, …), health (<c>healthy</c>, … or empty) and exit code.</returns>
    /// <exception cref="InvalidOperationException">Docker is not installed or not on the path.</exception>
    public IReadOnlyList<(string Service, string State, string Health, int ExitCode)> Containers()
    {
        using var process = Start(redirect: true, "--profile", "app", "ps", "-a", "--format", "json");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        var text = output.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        // Newer docker compose writes one JSON object per line, older versions one array.
        var items = text.StartsWith('[')
            ? JsonDocument.Parse(text).RootElement.EnumerateArray().Select(element => element.Clone()).ToList()
            : [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.Clone())];
        return [.. items.Select(item => (
                item.GetProperty("Service").GetString() ?? "?",
                item.TryGetProperty("State", out var state) ? state.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("Health", out var health) ? health.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("ExitCode", out var exit) && exit.ValueKind == JsonValueKind.Number ? exit.GetInt32() : 0))
            .OrderBy(item => item.Item1, StringComparer.Ordinal)];
    }

    private Process Start(bool redirect, params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = redirect };
        foreach (var argument in (string[])["compose", "-f", ComposeFile.Path, .. arguments])
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(start) ?? throw new InvalidOperationException("docker could not be started.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException($"docker is not available ({exception.Message}); install Docker Desktop or Docker Engine with the compose plugin.");
        }
    }
}
