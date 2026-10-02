using System.Security.Cryptography.X509Certificates;
using SuperApp.Gateway.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace SuperApp.Gateway.Bff.DataProtection;

/// <summary>
/// Configures ASP.NET Core Data Protection for the <c>bff-web</c> profile: one key ring shared by all replicas, stored in MSSQL
/// (<see cref="SuperApp.Gateway.Persistence.GatewayDbContext.DataProtectionKeys"/>) and encrypted at rest with a certificate from Vault (ADR-0013).
/// </summary>
/// <remarks>
/// <para>
/// Data Protection encrypts the session cookie, the OIDC correlation/nonce cookies and the session tickets in <c>gateway.Sessions</c>
/// (<see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore"/>). Every replica must use the same keys and the same <see cref="ApplicationName"/>, otherwise a request
/// routed to another pod cannot read the cookie and the user is logged out. Changing <see cref="ApplicationName"/> invalidates all sessions.
/// </para>
/// <para>Settings:</para>
/// <list type="bullet">
///   <item><description><c>DataProtection:CertificatePath</c>: PKCS#12 file used to encrypt new keys. Required outside the Development
///   environment; without it the application fails at startup. In Development the keys are stored unencrypted.</description></item>
///   <item><description><c>DataProtection:CertificatePassword</c>: password of that file (and of the previous ones).</description></item>
///   <item><description><c>DataProtection:PreviousCertificatePaths</c>: certificates that are only used to decrypt existing keys after the
///   certificate has been rotated. Keep them until all keys encrypted with them have expired.</description></item>
/// </list>
/// </remarks>
internal static class GatewayDataProtection
{
    /// <summary>Data Protection application name that isolates the BFF key ring; must be identical on all replicas.</summary>
    public const string ApplicationName = "superapp-gateway-bff-web";

    /// <summary>
    /// Registers Data Protection with keys persisted in <see cref="SuperApp.Gateway.Persistence.GatewayDbContext"/> and protected by the configured certificate,
    /// as described on <see cref="SuperApp.Gateway.Bff.DataProtection.GatewayDataProtection"/>.
    /// </summary>
    /// <param name="services">The service collection of the gateway.</param>
    /// <param name="configuration">Configuration containing the <c>DataProtection</c> section.</param>
    /// <param name="environment">Host environment; only Development may run without a certificate.</param>
    /// <returns>The Data Protection builder, for further configuration.</returns>
    /// <exception cref="InvalidOperationException"><c>DataProtection:CertificatePath</c> is not set outside the Development environment.</exception>
    public static IDataProtectionBuilder AddGatewayDataProtection(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var builder = services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<GatewayDbContext>();

        var certificatePath = configuration["DataProtection:CertificatePath"];
        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            return environment.IsDevelopment()
                ? builder
                : throw new InvalidOperationException(
                    "Klucze Data Protection muszą być szyfrowane certyfikatem: ustaw DataProtection:CertificatePath (ADR-0013).");
        }

        var password = configuration["DataProtection:CertificatePassword"];
        builder.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password));

        var previous = configuration.GetSection("DataProtection:PreviousCertificatePaths").Get<string[]>() ?? [];
        if (previous.Length > 0)
        {
            builder.UnprotectKeysWithAnyCertificate([.. previous.Select(path => X509CertificateLoader.LoadPkcs12FromFile(path, password))]);
        }

        return builder;
    }
}
