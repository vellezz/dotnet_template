using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SuperApp.Framework.Infrastructure.Analytics;

/// <summary>
/// Turns a CIAM subject (<c>sub</c> claim) into the pseudonymous identifier under which the user is known in PostHog (ADR-0036).
/// </summary>
/// <remarks>
/// <para>
/// PostHog never receives the CIAM subject, e-mail or name. The identifier is <c>HMAC-SHA256(IdKey, sub)</c>, written as 32 lowercase
/// hexadecimal characters with the prefix <c>u_</c> (128 bits of the MAC): stable for the same user in every process of an environment,
/// and impossible to link back to the CIAM account without <see cref="AnalyticsOptions.IdKey"/>, which stays in Vault.
/// </para>
/// <para>
/// The same identifier is used everywhere a person appears: the BFF returns it from <c>/bff/user</c> as <c>analyticsId</c> (the web
/// client passes it to <c>posthog.identify</c>), the mobile gateway from <c>GET /analytics/id</c>, services use it to evaluate feature
/// flags, and the analytics forwarder uses it for backend events. That is what lets funnels and flags work across web, mobile and backend.
/// </para>
/// <para>
/// Events without a user (system work in a Worker) use <see cref="System"/>. When analytics is disabled, <see cref="ForSubject"/> returns
/// <see langword="null"/> and clients must not initialize PostHog.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var analyticsId = identity.ForSubject(user.FindFirst("sub")?.Value); // "u_3f9c..." or null
/// </code>
/// </example>
/// <param name="options">Analytics settings; <see cref="AnalyticsOptions.IdKey"/> is the HMAC key.</param>
public sealed class AnalyticsIdentity(IOptions<AnalyticsOptions> options)
{
    /// <summary>Identifier of events and flag evaluations that have no user: <c>system</c>.</summary>
    public const string System = "system";

    private const string Prefix = "u_";

    /// <summary>Returns the analytics identifier of a CIAM subject.</summary>
    /// <param name="subject">The <c>sub</c> claim of the user's token, or <see langword="null"/> when there is no user.</param>
    /// <returns>
    /// <c>u_</c> followed by 32 lowercase hexadecimal characters; <see langword="null"/> when <paramref name="subject"/> is null or empty, or
    /// when analytics is disabled.
    /// </returns>
    public string? ForSubject(string? subject)
    {
        var settings = options.Value;
        if (string.IsNullOrEmpty(subject) || !settings.Enabled || string.IsNullOrEmpty(settings.IdKey))
        {
            return null;
        }

        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.IdKey), Encoding.UTF8.GetBytes(subject));
        return Prefix + Convert.ToHexStringLower(mac, 0, 16);
    }
}
