namespace SuperApp.Cli.Repository;

/// <summary>What the repository says about one domain service (bounded context) in <c>src/Services/{Name}</c>.</summary>
/// <param name="Name">Service name in PascalCase, e.g. <c>Knowledge</c>; the schema, compose and chart names are its lower-case form.</param>
/// <param name="Directory">Directory of the service relative to the repository root.</param>
/// <param name="Experience">Experience the service belongs to, from <c>experience:</c> in its Helm values; <see langword="null"/> when missing.</param>
/// <param name="ApiPort">Local HTTP port of <c>{Name}.Api</c> from its launch profile.</param>
/// <param name="WorkerPort">Local HTTP port of <c>{Name}.Worker</c> from its launch profile.</param>
/// <param name="Scopes">Scope values declared in <c>{Name}Scopes</c>, e.g. <c>knowledge.catalog.read</c>.</param>
/// <param name="EventIdRanges">Ranges of the EventId register that belong to the service.</param>
/// <param name="IntegrationEvents">Types of <c>{Name}.Contracts</c> (published language), e.g. <c>MaterialArchivedV1</c>.</param>
/// <param name="Consumers">Consumers in <c>{Name}.Worker/Consumers</c>.</param>
/// <param name="UsedByBffs">Experiences whose BFF has a generated client of this service (<c>Clients/{Name}</c>).</param>
internal sealed record ServiceInfo(
    string Name,
    string Directory,
    string? Experience,
    int? ApiPort,
    int? WorkerPort,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<EventIdRange> EventIdRanges,
    IReadOnlyList<string> IntegrationEvents,
    IReadOnlyList<string> Consumers,
    IReadOnlyList<string> UsedByBffs)
{
    /// <summary>Lower-case name used for the schema, compose services, chart values and scope prefix, e.g. <c>knowledge</c>.</summary>
    public string Key => Name.ToLowerInvariant();
}
