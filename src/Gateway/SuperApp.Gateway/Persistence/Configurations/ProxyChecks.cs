namespace SuperApp.Gateway.Persistence.Configurations;

/// <summary>SQL fragments of CHECK constraints shared by several route configuration tables (ADR-0022).</summary>
internal static class ProxyChecks
{
    /// <summary>
    /// Allows only the two gateway profiles (<see cref="SuperApp.Gateway.Hosting.GatewayProfiles.BffWeb"/>, <see cref="SuperApp.Gateway.Hosting.GatewayProfiles.Mobile"/>) in the
    /// <c>Profile</c> column. Changing it changes the model and therefore requires a migration.
    /// </summary>
    public const string Profile = "[Profile] IN ('bff-web','gateway-mobile')";

    /// <summary>
    /// Allows in the <c>Address</c> column of <c>gateway.Destinations</c> only the address of a Kubernetes Service inside the cluster:
    /// <c>http://{service}.{namespace}.svc.cluster.local:{port}</c> with an optional trailing <c>/</c> (constraint
    /// <c>CK_Destinations_ClusterAddress</c>, ADR-0022). The same rule is checked in code by
    /// <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyDestinationAddress"/> before a configuration is applied.
    /// </summary>
    /// <remarks>
    /// <para>SQL Server has no regular expressions, so the rule is a conjunction of simple conditions that together allow only that shape:</para>
    /// <list type="bullet">
    ///   <item><description>binary collation, so the character classes are case-sensitive: only lowercase letters, digits, <c>-</c>,
    ///   <c>.</c>, <c>:</c> and <c>/</c> are allowed, which excludes user info (<c>@</c>), query (<c>?</c>), fragment (<c>#</c>),
    ///   percent-encoding and whitespace;</description></item>
    ///   <item><description>the prefix <c>http://</c>, two labels starting with a letter or digit, then <c>.svc.cluster.local:</c> and a port
    ///   starting with a non-zero digit;</description></item>
    ///   <item><description>exactly four dots (so the host has exactly two labels before <c>.svc.cluster.local</c> and nothing follows it),
    ///   exactly two colons, and no label ending with <c>-</c>;</description></item>
    ///   <item><description>exactly two slashes, or three when the third one is the last character (no path);</description></item>
    ///   <item><description>the text after <c>.svc.cluster.local:</c>, without the trailing slash, is an integer from 1 to 65535
    ///   (<c>TRY_CAST</c> returns <c>NULL</c> for anything else, which <c>ISNULL</c> turns into 0: a CHECK constraint passes when its
    ///   condition is <c>UNKNOWN</c>, so a bare <c>NULL BETWEEN ...</c> would let such an address through).</description></item>
    /// </list>
    /// <para>The 63-character limit of a DNS label is checked only in code. Changing this text changes the model and requires a migration.</para>
    /// </remarks>
    public const string ClusterAddress =
        "[Address] COLLATE Latin1_General_BIN2 LIKE 'http://[a-z0-9]%.[a-z0-9]%.svc.cluster.local:[1-9]%'"
        + " AND [Address] COLLATE Latin1_General_BIN2 NOT LIKE '%[^-a-z0-9.:/]%'"
        + " AND [Address] NOT LIKE '%-.%'"
        + " AND LEN([Address]) - LEN(REPLACE([Address], '.', '')) = 4"
        + " AND LEN([Address]) - LEN(REPLACE([Address], ':', '')) = 2"
        + " AND (LEN([Address]) - LEN(REPLACE([Address], '/', '')) = 2"
        + " OR (LEN([Address]) - LEN(REPLACE([Address], '/', '')) = 3 AND [Address] LIKE '%/'))"
        + " AND ISNULL(TRY_CAST(REPLACE(SUBSTRING([Address], CHARINDEX('.svc.cluster.local:', [Address]) + 19, 500), '/', '') AS int), 0) BETWEEN 1 AND 65535";
}
