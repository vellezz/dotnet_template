namespace SuperApp.Cli.Repository;

/// <summary>A <c>[LoggerMessage]</c> event ID found in the source code.</summary>
/// <param name="Id">The event ID.</param>
/// <param name="Project">Name of the project that declares it, e.g. <c>Knowledge.Worker</c>.</param>
/// <param name="File">Source file relative to the repository root.</param>
/// <param name="Line">1-based line of the attribute.</param>
internal sealed record EventIdUsage(int Id, string Project, string File, int Line);
