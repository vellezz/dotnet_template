namespace SuperApp.Framework.Infrastructure.Http.ClientCredentials;

/// <summary>
/// Settings of one OAuth client used for service-to-service calls, bound from <c>ClientCredentials:{clientName}</c> (ADR-0007, ADR-0011).
/// </summary>
/// <remarks>
/// Each calling service has its own confidential client in the CIAM (<c>{service}-client</c>) that may request only the scopes it needs.
/// The secret is provided by Vault / External Secrets as an environment variable (<c>ClientCredentials__{clientName}__ClientSecret</c>),
/// never in <c>appsettings*.json</c>.
/// </remarks>
public sealed class ClientCredentialsOptions
{
    /// <summary>Token endpoint of the CIAM (from its OIDC discovery document). Required; validated when the host starts.</summary>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>Client identifier registered in the CIAM, by convention <c>{service}-client</c> of the calling service.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client secret. Supplied only from Vault / External Secrets; never commit it to configuration files.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Space-separated scopes to request, e.g. <c>knowledge.system.read</c>; <see langword="null"/> or empty requests the client's default scopes.</summary>
    public string? Scope { get; set; }
}
