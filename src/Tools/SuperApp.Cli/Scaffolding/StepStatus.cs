namespace SuperApp.Cli.Scaffolding;

/// <summary>What a scaffolding step did to one file or process.</summary>
internal enum StepStatus
{
    /// <summary>The step would run (<c>--dry-run</c>, or <c>remove</c> without <c>--yes</c>); nothing was changed.</summary>
    Planned,

    /// <summary>An existing file was changed.</summary>
    Changed,

    /// <summary>A file or directory was created.</summary>
    Created,

    /// <summary>A file or directory was deleted.</summary>
    Deleted,

    /// <summary>An external command ran successfully.</summary>
    Ran,

    /// <summary>The change was already there (the command is idempotent).</summary>
    Unchanged,
}
