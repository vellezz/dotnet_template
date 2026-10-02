using System.CommandLine;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Commands;

/// <summary>Options every command accepts (<c>--root</c>, <c>--json</c>) and the helpers that read them.</summary>
/// <remarks>Both options are recursive: they may be written before or after the subcommand.</remarks>
internal sealed class CommonOptions
{
    /// <summary>Directory inside the repository to start the root search from; the current directory by default.</summary>
    public Option<DirectoryInfo?> Root { get; } = new("--root")
    {
        Description = "Directory inside the repository (default: the current directory); the root is the nearest parent with SuperApp.slnx.",
        Recursive = true,
    };

    /// <summary>Writes machine-readable JSON instead of text, for CI and assistants.</summary>
    public Option<bool> Json { get; } = new("--json")
    {
        Description = "Write JSON instead of text (for CI and assistants such as Copilot).",
        Recursive = true,
    };

    /// <summary>Finds the repository root for the parsed command, or writes an error and returns <see langword="null"/>.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="output">Writer of the command output.</param>
    /// <returns>The full path of the root, or <see langword="null"/> when there is none (exit with <see cref="ExitCodes.NotFound"/>).</returns>
    public string? FindRoot(ParseResult parseResult, OutputWriter output)
    {
        var start = parseResult.GetValue(Root)?.FullName ?? Directory.GetCurrentDirectory();
        var root = RepositoryRoot.Find(start);
        if (root is null)
        {
            output.Error($"No {RepositoryRoot.MarkerFile} found in {start} or any parent directory; run the tool inside the repository or pass --root.");
        }

        return root;
    }

    /// <summary>Creates the writer for the parsed command line, in JSON or text mode.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>The writer over the configured output.</returns>
    public OutputWriter Output(ParseResult parseResult) =>
        new(parseResult.InvocationConfiguration.Output, parseResult.InvocationConfiguration.Error, parseResult.GetValue(Json));
}
