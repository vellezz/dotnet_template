namespace SuperApp.Cli.Repository;

/// <summary>A row of the EventId register <c>docs/logowanie-eventid.md</c>: a range of log event IDs owned by one component.</summary>
/// <param name="Start">First ID of the range.</param>
/// <param name="End">Last ID of the range, inclusive.</param>
/// <param name="Component">Text of the component column, e.g. <c>Knowledge (Worker)</c> or <c>`SuperApp.Framework.Infrastructure` (cache)</c>.</param>
/// <param name="Documented">IDs listed in the "used" column; ranges such as <c>3001–3004</c> are expanded.</param>
internal sealed record EventIdRange(int Start, int End, string Component, IReadOnlySet<int> Documented)
{
    /// <summary>Whether <paramref name="id"/> lies in the range.</summary>
    /// <param name="id">A log event ID.</param>
    /// <returns><see langword="true"/> when <see cref="Start"/> ≤ <paramref name="id"/> ≤ <see cref="End"/>.</returns>
    public bool Contains(int id) => id >= Start && id <= End;

    /// <summary>
    /// Whether the range belongs to <paramref name="project"/>: the component column names the project (<c>SuperApp.Gateway</c>) or, for a
    /// service or BFF project, its first name segment (<c>Knowledge</c> for <c>Knowledge.Worker</c>, <c>Example.Bff</c> for <c>Example.Bff</c>).
    /// </summary>
    /// <param name="project">Project name, e.g. <c>Knowledge.Worker</c>.</param>
    /// <returns><see langword="true"/> when the project may use IDs of this range.</returns>
    public bool BelongsTo(string project) =>
        Component.Contains(project, StringComparison.Ordinal)
        || (!project.StartsWith("SuperApp.", StringComparison.Ordinal)
            && Component.Contains(project.Split('.')[0], StringComparison.Ordinal));
}
