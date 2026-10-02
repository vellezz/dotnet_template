using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor.Rules;

/// <summary>No two components use the same local port.</summary>
/// <remarks>
/// Ports come from launch profiles and from host ports published by docker compose. The launch profile and the compose service of one
/// component have the same name and deliberately share a port (the component runs either in the IDE or in a container); a port used by
/// two different names is a conflict that shows up only when both run.
/// </remarks>
internal sealed class PortsRule : IDoctorRule
{
    /// <inheritdoc />
    public string Id => "ports";

    /// <inheritdoc />
    public string Description => "Launch profiles and docker compose do not give one local port to two components.";

    /// <inheritdoc />
    public IEnumerable<Finding> Check(RepositoryModel model, DoctorSettings settings) =>
        model.Ports
            .GroupBy(port => port.Port)
            .Where(group => group.Select(port => port.Owner).Distinct(StringComparer.Ordinal).Count() > 1)
            .OrderBy(group => group.Key)
            .Select(group => new Finding(
                Id,
                Severity.Error,
                $"Port {group.Key} is used by {string.Join(", ", group.Select(port => $"{port.Owner} ({port.Source})").Distinct(StringComparer.Ordinal))}.",
                group.First().Source,
                Fix: "Give one of them the next free port (dotnet superapp list ports) in its launchSettings.json and docker-compose.yml."));
}
