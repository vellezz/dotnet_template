namespace SuperApp.Cli;

/// <summary>Exit codes of <c>dotnet superapp</c>, the same for every command, so scripts, CI and assistants can branch on them.</summary>
internal static class ExitCodes
{
    /// <summary>The command succeeded; for <c>doctor</c>: no errors (warnings allowed).</summary>
    public const int Success = 0;

    /// <summary>Invalid arguments (reported by the command line parser).</summary>
    public const int InvalidArguments = 1;

    /// <summary><c>doctor</c> found at least one error.</summary>
    public const int FindingsFound = 2;

    /// <summary>No repository root (<c>SuperApp.slnx</c>) above the directory, or the requested service or BFF does not exist.</summary>
    public const int NotFound = 3;

    /// <summary>
    /// A scaffolding command (<c>add</c>, <c>remove</c>) stopped: a precondition is not met or an external step failed. The message says why
    /// and lists the steps already applied.
    /// </summary>
    public const int Failed = 4;
}
