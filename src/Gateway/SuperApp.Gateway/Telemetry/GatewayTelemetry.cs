using System.Diagnostics.Metrics;

namespace SuperApp.Gateway.Telemetry;

/// <summary>
/// OpenTelemetry metrics of the gateway (ADR-0022): whether a replica has a loaded route configuration and how often reloading failed.
/// The meter is added to the OpenTelemetry meter provider in <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// The gauge <c>superapp.gateway.proxy_config.loaded</c> (tags <c>profile</c> and <c>migration_id</c>) is created by
/// <see cref="SuperApp.Gateway.Proxy.Configuration.DatabaseProxyConfigProvider"/>; comparing <c>migration_id</c> across replicas shows whether a route change has reached
/// all of them. Alert on <see cref="ProxyConfigReloadFailures"/>: a failed reload means the replica keeps serving its previous configuration.
/// </remarks>
internal static class GatewayTelemetry
{
    /// <summary>Name of the gateway meter, used to register it with OpenTelemetry.</summary>
    public const string MeterName = "SuperApp.Gateway";

    /// <summary>The gateway meter; all gateway instruments are created on it.</summary>
    public static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Counter <c>superapp.gateway.proxy_config.reload_failures</c>: incremented when a new route configuration is rejected by validation
    /// or when reading it from the database fails.
    /// </summary>
    public static readonly Counter<long> ProxyConfigReloadFailures =
        Meter.CreateCounter<long>("superapp.gateway.proxy_config.reload_failures", description: "Nieudane przeładowania konfiguracji tras (walidacja lub baza)");
}
