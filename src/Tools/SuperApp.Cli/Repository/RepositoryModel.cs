namespace SuperApp.Cli.Repository;

/// <summary>Everything the commands need to know about the repository, read once by <see cref="RepositoryScanner"/>.</summary>
/// <remarks>
/// The model describes what is in the files, not what should be there: <c>doctor</c> rules compare its parts with each other and with the
/// files, <c>list</c> and <c>info</c> print them. It never changes the repository.
/// </remarks>
/// <param name="Files">Access to the files of the repository.</param>
/// <param name="Services">Domain services in <c>src/Services</c>, sorted by name.</param>
/// <param name="Bffs">BFFs in <c>src/Bff</c>, sorted by name.</param>
/// <param name="Ports">Ports from launch profiles and docker compose.</param>
/// <param name="EventIdRanges">Rows of the EventId register.</param>
/// <param name="EventIds">Event IDs used in the source code.</param>
/// <param name="Compose">Services of the local docker compose file.</param>
internal sealed record RepositoryModel(
    RepositoryFiles Files,
    IReadOnlyList<ServiceInfo> Services,
    IReadOnlyList<BffInfo> Bffs,
    IReadOnlyList<PortUsage> Ports,
    IReadOnlyList<EventIdRange> EventIdRanges,
    IReadOnlyList<EventIdUsage> EventIds,
    IReadOnlyList<ComposeService> Compose)
{
    /// <summary>Names of all experiences: those with a BFF and those named by a service's Helm values, sorted.</summary>
    public IReadOnlyList<string> Experiences =>
        [.. Bffs.Select(bff => bff.Key).Concat(Services.Select(service => service.Experience).OfType<string>())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
