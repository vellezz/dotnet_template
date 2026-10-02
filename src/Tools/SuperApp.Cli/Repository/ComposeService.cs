namespace SuperApp.Cli.Repository;

/// <summary>A service of the local <c>docker-compose.yml</c> with the host ports it publishes and its environment variable names.</summary>
/// <param name="Name">Service name, e.g. <c>knowledge-api</c>; matches the launch profile of the project.</param>
/// <param name="HostPorts">Host side of every <c>"host:container"</c> port mapping.</param>
/// <param name="Environment">Names of the environment variables set on the service, e.g. <c>Downstream__Knowledge__BaseAddress</c>.</param>
internal sealed record ComposeService(string Name, IReadOnlyList<int> HostPorts, IReadOnlyList<string> Environment);
