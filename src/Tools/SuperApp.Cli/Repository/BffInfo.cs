namespace SuperApp.Cli.Repository;

/// <summary>What the repository says about the BFF of one experience in <c>src/Bff/{Name}.Bff</c> (ADR-0038).</summary>
/// <param name="Name">Experience name in PascalCase, e.g. <c>Example</c>.</param>
/// <param name="Directory">Directory of the BFF project relative to the repository root.</param>
/// <param name="Port">Local HTTP port from the launch profile.</param>
/// <param name="Scopes">Scopes of its internal API declared in <c>{Name}BffScopes</c>, e.g. <c>example.internal.read</c>.</param>
/// <param name="Clients">Services it has a generated client of (<c>Clients/{Service}</c>).</param>
/// <param name="EventIdRanges">Ranges of the EventId register that belong to the BFF.</param>
internal sealed record BffInfo(
    string Name,
    string Directory,
    int? Port,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Clients,
    IReadOnlyList<EventIdRange> EventIdRanges)
{
    /// <summary>Lower-case experience name used for routes, compose, chart values and the namespace, e.g. <c>example</c>.</summary>
    public string Key => Name.ToLowerInvariant();

    /// <summary>Project name, e.g. <c>Example.Bff</c>.</summary>
    public string Project => $"{Name}.Bff";
}
