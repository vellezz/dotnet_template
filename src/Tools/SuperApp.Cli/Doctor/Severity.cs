namespace SuperApp.Cli.Doctor;

/// <summary>How serious a <see cref="Finding"/> is.</summary>
internal enum Severity
{
    /// <summary>Drift that does not break anything yet (e.g. the EventId register lists an ID nobody uses); does not fail <c>doctor</c>.</summary>
    Warning,

    /// <summary>Something is missing or inconsistent and will break a build, a deployment or the local environment; fails <c>doctor</c>.</summary>
    Error,
}
