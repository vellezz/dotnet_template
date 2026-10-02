using Yarp.ReverseProxy.Forwarder;

namespace SuperApp.Gateway.Proxy.Resilience;

/// <summary>
/// Replacement for YARP's default <see cref="IForwarderHttpClientFactory"/> that puts an <see cref="SuperApp.Gateway.Proxy.Resilience.IdempotentRetryHandler"/> in front of
/// the HTTP handler of every cluster, so retries apply to all routes without any database configuration (retry is a code-only concern, ADR-0022).
/// </summary>
/// <remarks>
/// Registered with <c>Services.Replace</c> in <c>Program.cs</c>. YARP creates a client per cluster and recreates it when the cluster
/// configuration changes; the retry settings are read at that moment:
/// <c>Gateway:Retry:MaxRetries</c> (default 2) and <c>Gateway:Retry:BaseDelay</c> (default 100 ms).
/// </remarks>
/// <param name="configuration">Application configuration with the <c>Gateway:Retry</c> section.</param>
/// <param name="loggerFactory">Creates the loggers of the factory and of the retry handler.</param>
internal sealed class IdempotentRetryForwarderHttpClientFactory(IConfiguration configuration, ILoggerFactory loggerFactory)
    : ForwarderHttpClientFactory(loggerFactory.CreateLogger<ForwarderHttpClientFactory>())
{
    /// <summary>Wraps YARP's handler chain for a cluster with an <see cref="SuperApp.Gateway.Proxy.Resilience.IdempotentRetryHandler"/> as the outermost handler.</summary>
    /// <param name="context">YARP context of the cluster whose client is being created.</param>
    /// <param name="handler">The innermost socket handler created by YARP.</param>
    /// <returns>The retry handler whose inner handler is the chain YARP would use by default.</returns>
    protected override HttpMessageHandler WrapHandler(ForwarderHttpClientContext context, HttpMessageHandler handler) =>
        new IdempotentRetryHandler(
            configuration.GetValue("Gateway:Retry:MaxRetries", 2),
            configuration.GetValue("Gateway:Retry:BaseDelay", TimeSpan.FromMilliseconds(100)),
            loggerFactory.CreateLogger<IdempotentRetryHandler>())
        {
            InnerHandler = base.WrapHandler(context, handler),
        };
}
