namespace SuperApp.Gateway.Hosting;

/// <summary>
/// Names of the two gateway profiles. The same image is deployed twice (ADR-0006); the profile is selected with the
/// <c>Gateway:Profile</c> setting (default <see cref="BffWeb"/>) and decides the authentication scheme, whether the BFF endpoints and
/// the CSRF check are active, and which rows of the route configuration tables are loaded.
/// </summary>
/// <remarks>
/// The values are also stored in the <c>Profile</c> column of every route configuration table and enforced there by a CHECK constraint
/// (<see cref="SuperApp.Gateway.Persistence.Configurations.ProxyChecks.Profile"/>), so renaming a profile requires a migration. Any other value of <c>Gateway:Profile</c>
/// stops the application at startup.
/// </remarks>
public static class GatewayProfiles
{
    /// <summary>
    /// BFF for the Angular application: session in an HttpOnly cookie, tokens held only on the server (ADR-0011, ADR-0013),
    /// <c>X-CSRF</c> header required, <c>/bff/*</c> endpoints mapped.
    /// </summary>
    public const string BffWeb = "bff-web";

    /// <summary>Gateway for the mobile applications: validates the JWT bearer token sent by the app and forwards it unchanged (ADR-0012).</summary>
    public const string Mobile = "gateway-mobile";
}
