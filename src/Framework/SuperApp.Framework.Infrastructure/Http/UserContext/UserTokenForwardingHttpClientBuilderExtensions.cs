using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SuperApp.Framework.Infrastructure.Http.UserContext;

/// <summary>Configures an HTTP client to make calls in the current user's context (ADR-0040).</summary>
/// <remarks>
/// <para>Two ways of authenticating an outgoing call exist in this system, chosen per client:</para>
/// <list type="bullet">
///   <item><description><b>In the user's context (default):</b> <see cref="AddUserTokenForwarding"/>. The callee sees the user and applies
///   its resource rules (owner from <c>sub</c>, someone else's resource = 404). Used by a BFF calling the domain services of its experience,
///   by a BFF of another experience calling our internal BFF API, and by a service calling another service of the same experience.</description></item>
///   <item><description><b>System call (exception):</b> <c>AddClientCredentialsToken</c>, only for operations without a user, with a
///   <c>{service}.system.*</c> or <c>{experience}.internal.system.*</c> scope (ADR-0040, ADR-0042).</description></item>
/// </list>
/// <para>
/// Which components may call which is decided by the network (NetworkPolicy, ADR-0041), not by the token.
/// </para>
/// </remarks>
public static class UserTokenForwardingHttpClientBuilderExtensions
{
    /// <summary>Sends the current user's access token with every call of this client.</summary>
    /// <remarks>
    /// Registers <see cref="IDownstreamTokenProvider"/> (the incoming token passed on unchanged) unless another implementation is already
    /// registered, for example a future token exchange. Requires <c>IHttpContextAccessor</c>, which <c>AddAppApi</c> registers.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddDownstreamApi&lt;IKnowledgeApi&gt;(builder.Configuration, "Knowledge").AddUserTokenForwarding();
    /// </code>
    /// </example>
    /// <param name="builder">The client being configured.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static IHttpClientBuilder AddUserTokenForwarding(this IHttpClientBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton<IDownstreamTokenProvider, ForwardedUserTokenProvider>();
        builder.Services.TryAddTransient<UserTokenForwardingHandler>();
        return builder.AddHttpMessageHandler<UserTokenForwardingHandler>();
    }
}
