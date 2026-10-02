namespace SuperApp.Cli.Repository;

/// <summary>A local port taken by a component: a launch profile URL or a host port published by docker compose.</summary>
/// <param name="Port">The TCP port.</param>
/// <param name="Owner">
/// Component name: the launch profile name or the compose service name. Both use the same names (<c>knowledge-api</c>), so the IDE and
/// the container of one component may share a port, while two components may not.
/// </param>
/// <param name="Source">File the port comes from, relative to the repository root.</param>
internal sealed record PortUsage(int Port, string Owner, string Source);
