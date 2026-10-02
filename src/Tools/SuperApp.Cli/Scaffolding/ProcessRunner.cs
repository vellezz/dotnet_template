using System.Diagnostics;

namespace SuperApp.Cli.Scaffolding;

/// <summary>Runs external commands (<c>dotnet new</c>, <c>dotnet ef</c>, <c>dotnet refitter</c>, <c>git</c>) in the repository root.</summary>
/// <remarks>
/// Output is captured and shown only when the command fails, so a successful command adds one line to the report. A non-zero exit code
/// becomes a <see cref="ScaffoldException"/> with the command and its output.
/// </remarks>
/// <param name="workingDirectory">Directory the commands run in: the repository root.</param>
internal sealed class ProcessRunner(string workingDirectory)
{
    /// <summary>Runs <c>dotnet</c> with the given arguments and waits for it.</summary>
    /// <param name="arguments">Arguments, each passed as one argument (no shell quoting needed).</param>
    /// <returns>The report line of the command.</returns>
    /// <exception cref="ScaffoldException">The command could not start or exited with a non-zero code.</exception>
    public StepResult Dotnet(params string[] arguments)
    {
        Capture(arguments);
        return new StepResult(StepStatus.Ran, "dotnet " + string.Join(' ', arguments), "ok");
    }

    /// <summary>Runs <c>dotnet</c> with the given arguments and returns what it wrote to the standard output.</summary>
    /// <param name="arguments">Arguments, each passed as one argument.</param>
    /// <returns>The standard output.</returns>
    /// <exception cref="ScaffoldException">The command could not start or exited with a non-zero code.</exception>
    public string Capture(params string[] arguments) => Run("dotnet", arguments);

    /// <summary>Runs <c>git</c> with the given arguments and returns what it wrote to the standard output.</summary>
    /// <param name="arguments">Arguments, each passed as one argument.</param>
    /// <returns>The standard output.</returns>
    /// <exception cref="ScaffoldException">Git is not installed, the directory is not a repository or the command failed.</exception>
    public string Git(params string[] arguments) => Run("git", arguments);

    private string Run(string program, string[] arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";

        var command = program + " " + string.Join(' ', arguments);
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new ScaffoldException($"Could not start: {command}");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new ScaffoldException($"Could not start {program}: {exception.Message}");
        }

        using var started = process;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new ScaffoldException($"{command} failed with exit code {process.ExitCode}:\n{output.Result}{error.Result}");
        }

        return output.Result;
    }
}
