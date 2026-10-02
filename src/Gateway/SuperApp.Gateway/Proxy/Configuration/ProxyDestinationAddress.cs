using System.Globalization;
using System.Text.RegularExpressions;

namespace SuperApp.Gateway.Proxy.Configuration;

/// <summary>
/// Code-side check of a cluster destination address: the gateway may forward only to a Kubernetes Service inside the cluster,
/// <c>http://{service}.{namespace}.svc.cluster.local:{port}</c>, optionally followed by a single <c>/</c> (ADR-0022).
/// </summary>
/// <remarks>
/// <para>
/// A destination address decides where requests (with the user's bearer token) are sent, so a wrong address is a security problem, not
/// just a routing error. The rule is enforced twice: by the database constraint <c>CK_Destinations_ClusterAddress</c>
/// (<see cref="SuperApp.Gateway.Persistence.Configurations.ProxyChecks.ClusterAddress"/>) when a migration writes the row, and by this class in
/// <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigMapper"/> before a configuration is applied. The second check also protects against a
/// cached snapshot or a database whose constraint was disabled.
/// </para>
/// <para>
/// Rules: scheme <c>http</c> only (traffic inside the cluster; TLS ends at the Ingress); <c>{service}</c> and <c>{namespace}</c> are DNS
/// labels (lowercase letters, digits and <c>-</c>, starting and ending with a letter or digit, at most 63 characters); the port is required,
/// from 1 to 65535, without leading zeros; no user info, path, query or fragment. Addresses such as
/// <c>http://evil.com/x.svc.cluster.local</c> or <c>http://a.b.svc.cluster.local.evil.com</c> are rejected.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// ProxyDestinationAddress.IsValid("http://knowledge-api.knowledge.svc.cluster.local:8080");   // true
/// ProxyDestinationAddress.IsValid("http://knowledge-api.knowledge.svc.cluster.local:8080/");  // true
/// ProxyDestinationAddress.IsValid("https://knowledge-api.knowledge.svc.cluster.local:8080");  // false: https
/// ProxyDestinationAddress.IsValid("http://a.b.svc.cluster.local.evil.com:80");               // false: external host
/// </code>
/// </example>
internal static partial class ProxyDestinationAddress
{
    /// <summary>Checks whether <paramref name="address"/> is the address of a Kubernetes Service inside the cluster, as described on the class.</summary>
    /// <param name="address">The <see cref="SuperApp.Gateway.Persistence.Entities.ProxyDestination.Address"/> value; <see langword="null"/> is invalid.</param>
    /// <returns><see langword="true"/> when the address has exactly the allowed form; otherwise <see langword="false"/>.</returns>
    public static bool IsValid(string? address)
    {
        if (address is null)
        {
            return false;
        }

        var match = AddressPattern().Match(address);
        return match.Success
            && int.TryParse(match.Groups["port"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port <= 65535;
    }

    // \z instead of $: $ would also accept a trailing newline.
    [GeneratedRegex(
        @"^http://[a-z0-9](?:[-a-z0-9]{0,61}[a-z0-9])?\.[a-z0-9](?:[-a-z0-9]{0,61}[a-z0-9])?\.svc\.cluster\.local:(?<port>[1-9][0-9]{0,4})/?\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex AddressPattern();
}
