using SuperApp.Framework.Infrastructure.Analytics;
using SuperApp.Gateway.Analytics;
using SuperApp.Gateway.Bff.DataProtection;
using SuperApp.Framework.Infrastructure.HealthChecks;
using SuperApp.Gateway.Bff.Security;
using SuperApp.Gateway.Bff.Sessions;
using SuperApp.Gateway.Bff.Tokens;
using SuperApp.Gateway.Hosting;
using SuperApp.Gateway.Proxy.Configuration;
using SuperApp.Gateway.Proxy.Resilience;
using SuperApp.Gateway.Proxy.Transforms;
using SuperApp.Gateway.Security;
using SuperApp.Gateway.Telemetry;
using System.Security.Claims;
using SuperApp.Framework.Infrastructure.Caching;
using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Gateway.Bff;
using SuperApp.Gateway.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

// Composition root of the YARP gateway. One project, two deployments selected by Gateway:Profile (ADR-0006):
//   bff-web        - Backend for Frontend of the Angular SPA: cookie session + OIDC, tokens never reach the browser (ADR-0011, ADR-0013);
//   gateway-mobile - gateway for the Android/iOS apps: validates the JWT bearer token sent by the app and forwards it unchanged (ADR-0012).
//
// How a request travels through the bff-web profile (middleware order below matters):
//   1. CsrfHeaderMiddleware rejects /api/* without the "X-CSRF: 1" header (401); /bff/logout is a GET navigation protected by its sid parameter.
//   2. Routing picks either a /bff/* endpoint (BffEndpoints) or a YARP route loaded from the database (DatabaseProxyConfigProvider).
//   3. Request timeouts apply the per-route TimeoutSeconds from the database.
//   4. Cookie authentication reads the "__Host-bff" cookie, which only holds a random session key; DbTicketStore loads and decrypts
//      the authentication ticket (claims + access/refresh tokens) from gateway.Sessions. TokenRefresher (OnValidatePrincipal) refreshes
//      the access token shortly before it expires; if refreshing fails the session ends and the SPA receives 401.
//   5. Authorization evaluates the route policy named in gateway.Routes.AuthorizationPolicy (GatewayPolicies, scope-based,
//      coarse-grained); endpoints without a policy fall back to "authenticated user required".
//   6. Rate limiting applies the route's RateLimiterPolicy (GatewayRateLimits).
//   7. YARP forwards the request: SecurityTransforms strips Cookie and X-User-* headers and adds "Authorization: Bearer <access token>";
//      IdempotentRetryHandler retries GET/HEAD on transient errors. The domain service validates the token itself (ADR-0007).
// The gateway-mobile profile runs the same pipeline without steps 1 and 4 (JWT bearer authentication instead of the cookie session).

var builder = WebApplication.CreateBuilder(args);

var profile = builder.Configuration["Gateway:Profile"] ?? GatewayProfiles.BffWeb;
if (profile is not (GatewayProfiles.BffWeb or GatewayProfiles.Mobile))
{
    throw new InvalidOperationException($"Nieznany profil bramy '{profile}'.");
}

builder.AddAppServiceDefaults<GatewayDbContext>(profile);
builder.Services.AddDbContext<GatewayDbContext>(options => GatewayDbContextOptions.Configure(options, builder.Configuration.GetConnectionString("Gateway")));
builder.Services.AddAppCaching(builder.Configuration, "gateway");
builder.Services.AddAppAnalytics(builder.Configuration);

// Routes and clusters come from the gateway schema in MSSQL (ADR-0022). The provider is a singleton registered three times:
// as itself (health check), as YARP's IProxyConfigProvider and as a hosted service that polls for new migrations.
// Security transforms are code, never database configuration.
builder.Services.AddSingleton<DatabaseProxyConfigProvider>();
builder.Services.AddSingleton<IProxyConfigProvider>(provider => provider.GetRequiredService<DatabaseProxyConfigProvider>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<DatabaseProxyConfigProvider>());
builder.Services.AddHealthChecks().AddCheck<ProxyConfigLoadedHealthCheck>("proxy-config", tags: [HealthTags.Startup]);
builder.Services.AddReverseProxy().AddTransforms(context => SecurityTransforms.Apply(context, profile));
builder.Services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory, IdempotentRetryForwarderHttpClientFactory>());
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(GatewayTelemetry.MeterName));

GatewayPolicies.Register(builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
builder.Services.AddRateLimiter(options => GatewayRateLimits.Register(options, builder.Configuration));

// Per-route timeouts from the database (ProxyRoute.TimeoutSeconds, ADR-0022) are enforced by the request timeouts middleware.
builder.Services.AddRequestTimeouts();

if (profile == GatewayProfiles.BffWeb)
{
    AddBffWeb(builder);
}
else
{
    AddGatewayMobile(builder);
}

var app = builder.Build();

app.UseExceptionHandler();
// Empty error answers of the gateway itself get code and traceId; answers relayed from a BFF stay unchanged (ADR-0044).
app.UseStatusCodePages(GatewayStatusCodePages.WriteAsync);
if (profile == GatewayProfiles.BffWeb)
{
    app.UseMiddleware<CsrfHeaderMiddleware>();
}

app.UseRouting();
app.UseRequestTimeouts();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (profile == GatewayProfiles.BffWeb)
{
    app.MapBffEndpoints();
}

// PostHog proxy /ingest (bff-web) and GET /analytics/id (gateway-mobile); code-defined, never database routes (ADR-0036).
app.MapAnalyticsEndpoints(profile);

app.MapReverseProxy();
app.MapAppDefaultEndpoints();

await app.RunAsync();

// BFF web: cookie + OIDC (Authorization Code + PKCE, confidential client), server-side session in MSSQL, no tokens in the browser
// (ADR-0006, ADR-0011, ADR-0013).
static void AddBffWeb(WebApplicationBuilder builder)
{
    builder.Services.AddGatewayDataProtection(builder.Configuration, builder.Environment);

    builder.Services.AddHttpClient(TokenRefresher.HttpClientName);
    builder.Services.AddSingleton<TokenRefresher>();
    builder.Services.AddSingleton<DbTicketStore>();
    builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, TicketStoreCookieSetup>();
    builder.Services.AddHostedService<SessionCleanupService>();

    builder.Services
        .AddAuthentication(options =>
        {
            // No session on /api/* or /bff/user means 401 for the SPA (never a 302 to the CIAM, which an XHR cannot follow);
            // the redirect to the CIAM login page happens only through /bff/login.
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignOutScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "__Host-bff";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = builder.Configuration.GetValue("Gateway:SessionLifetime", TimeSpan.FromHours(8));
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = context =>
                context.HttpContext.RequestServices.GetRequiredService<TokenRefresher>().ValidatePrincipalAsync(context);
            options.Events.OnRedirectToLogin = context => Reject(context.Response, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => Reject(context.Response, StatusCodes.Status403Forbidden);
        })
        .AddOpenIdConnect(options =>
        {
            builder.Configuration.GetSection("Authentication").Bind(options);
            options.ResponseType = "code";
            options.UsePkce = true;
            options.SaveTokens = true;
            options.MapInboundClaims = false;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.TokenValidationParameters.NameClaimType = "name";
            // No offline_access: the refresh token stays bound to the CIAM SSO session, so logging out in the CIAM
            // (back-channel logout) also ends the BFF session. An offline token would survive the logout (ADR-0011).
            foreach (var scope in builder.Configuration.GetSection("Authentication:Scopes").Get<string[]>() ?? [])
            {
                options.Scope.Add(scope);
            }

            // The granted scope from the token response is stored as a "scope" claim in the session, so the per-route policies
            // (GatewayPolicies) evaluate the same claim as in gateway-mobile, where it comes from the JWT.
            options.Events.OnTokenValidated = context =>
            {
                if (context.TokenEndpointResponse?.Scope is { Length: > 0 } scope && context.Principal?.Identity is ClaimsIdentity identity)
                {
                    identity.AddClaim(new Claim("scope", scope));
                }

                return Task.CompletedTask;
            };
        });
}

// Gateway mobile: validates the JWT (issuer, audience, signature via the CIAM JWKS) from the Authentication section and forwards
// the same token unchanged (ADR-0006, ADR-0012).
static void AddGatewayMobile(WebApplicationBuilder builder) =>
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            builder.Configuration.GetSection("Authentication").Bind(options);
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ClockSkew = HostingExtensions.TokenClockSkew;
        });

// Replaces the cookie handler's redirects with plain status codes, which is what the SPA expects from an API.
static Task Reject(HttpResponse response, int statusCode)
{
    response.StatusCode = statusCode;
    return Task.CompletedTask;
}
