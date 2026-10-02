using SuperApp.Framework.Infrastructure.Api;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Telemetry;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Infrastructure.HealthChecks;
using SuperApp.Framework.Infrastructure.OpenApi;
using SuperApp.Framework.Infrastructure.Security;
using SuperApp.Framework.Infrastructure.Telemetry;
using SuperApp.Framework.Infrastructure.Time;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SuperApp.Framework.Infrastructure.Json;
using SuperApp.Framework.Infrastructure.ValueObjects;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SuperApp.Framework.Infrastructure.Hosting;

/// <summary>
/// Host setup shared by every API and Worker process: telemetry, clock, error responses, authentication, JSON contract and health endpoints.
/// </summary>
/// <remarks>
/// <para>A service's <c>Program.cs</c> composes these methods instead of configuring the host by hand, so every service behaves the same way:</para>
/// <code>
/// var builder = WebApplication.CreateBuilder(args);
///
/// builder.AddAppServiceDefaults&lt;KnowledgeWriteDbContext&gt;("knowledge-api");
/// builder.AddAppApi();
/// builder.Services.AddOpenApi(options =&gt; HostingExtensions.ConfigureOpenApi(options));
/// builder.Services.AddKnowledgeInfrastructure(builder.Configuration, OutboxDelivery.Disabled);
///
/// var app = builder.Build();
/// app.UseExceptionHandler();
/// app.UseStatusCodePages();
/// app.UseAuthentication();
/// app.UseAuthorization();
/// app.MapControllers();
/// app.MapOpenApi().AllowAnonymous();
/// app.MapAppDefaultEndpoints();
/// await app.RunAsync();
/// </code>
/// <para>A Worker uses <see cref="AddAppWorker"/> instead of <see cref="AddAppApi"/> and <see cref="Messaging.OutboxDelivery.Enabled"/>.</para>
/// </remarks>
public static class HostingExtensions
{
    /// <summary>
    /// Tolerance for clock differences when checking token lifetime: 30 seconds instead of the library default of 5 minutes.
    /// </summary>
    /// <remarks>
    /// With the default, an access token stays usable for up to 5 minutes after its <c>exp</c>, which for a 5-minute token doubles the window in
    /// which a token survives logout. Cluster nodes and the CIAM keep their clocks synchronized, so 30 seconds is enough. Used by every
    /// API (<see cref="AddAppApi"/>) and by the local edge gateway; the shared edge gateway gets the same value as a requirement.
    /// </remarks>
    public static readonly TimeSpan TokenClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers the part of <see cref="AddAppServiceDefaults{TWriteDbContext}"/> that does not depend on a database: OpenTelemetry, <see cref="IClock"/>,
    /// <c>ProblemDetails</c>, the shutdown readiness check and, if configured, the Redis dependency check.
    /// </summary>
    /// <remarks>
    /// Used directly by processes without their own database, such as the analytics forwarder (ADR-0036); services and the gateway call the
    /// generic overload, which calls this one and adds the migration and SQL Server checks. Call exactly one of the two: health check names
    /// must be unique, so calling both fails at startup.
    /// </remarks>
    /// <param name="builder">The host builder from <c>WebApplication.CreateBuilder</c>.</param>
    /// <param name="serviceName">Value of the OpenTelemetry <c>service.name</c> resource attribute, for example <c>analytics-forwarder</c>.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static IHostApplicationBuilder AddAppServiceDefaults(this IHostApplicationBuilder builder, string serviceName)
    {
        builder.AddAppTelemetry(serviceName);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IClock, SystemClock>();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsConventions.Apply);

        var health = builder.Services.AddHealthChecks()
            .AddCheck<ShutdownHealthCheck>("shutdown", tags: [HealthTags.Ready]);

        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Redis")))
        {
            health.AddCheck<DistributedCacheHealthCheck>("redis", tags: [HealthTags.Dependencies]);
        }

        return builder;
    }

    /// <summary>
    /// Registers what every process of a service needs: OpenTelemetry (traces, metrics, logs), <see cref="IClock"/>, <c>ProblemDetails</c>
    /// and the health checks used by the Kubernetes probes (ADR-0008, ADR-0018).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Traces cover ASP.NET Core, outgoing HTTP (including Refit clients and YARP), SQL Server commands, MassTransit, the application pipeline
    /// and domain event dispatch. Export over OTLP is enabled only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, so local runs and tests
    /// need no collector.
    /// </para>
    /// <para>
    /// Health checks: pending migrations of <typeparamref name="TWriteDbContext"/> (startup), shutdown state (ready), SQL Server and, if configured,
    /// Redis (dependencies). The process only checks migrations; it never applies them (ADR-0004).
    /// </para>
    /// </remarks>
    /// <typeparam name="TWriteDbContext">The service's write database context; the startup probe fails while it has pending migrations.</typeparam>
    /// <param name="builder">The host builder from <c>WebApplication.CreateBuilder</c>.</param>
    /// <param name="serviceName">
    /// Value of the OpenTelemetry <c>service.name</c> resource attribute, in the form <c>{service}-api</c> or <c>{service}-worker</c>;
    /// it is how traces and logs of this process are found in Grafana.
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static IHostApplicationBuilder AddAppServiceDefaults<TWriteDbContext>(this IHostApplicationBuilder builder, string serviceName)
        where TWriteDbContext : DbContext
    {
        builder.AddAppServiceDefaults(serviceName);
        builder.Services.AddHealthChecks()
            .AddCheck<MigrationsAppliedHealthCheck<TWriteDbContext>>("migrations", tags: [HealthTags.Startup])
            .AddDbContextCheck<TWriteDbContext>("mssql", tags: [HealthTags.Dependencies]);

        return builder;
    }

    /// <summary>
    /// Configures an API process: MVC controllers, the JSON contract, JWT bearer authentication with mandatory issuer and audience validation,
    /// <see cref="ICurrentUser"/> from the token, and a fallback policy requiring an authenticated user on every endpoint (ADR-0007, ADR-0019).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every service validates the access token itself (zero trust): the gateway forwards the token unchanged and identity headers are never trusted.
    /// Settings come from the <c>Authentication</c> configuration section (<c>Authority</c>, <c>Audience</c>, <c>RequireHttpsMetadata</c>).
    /// Endpoints that must be public need an explicit <c>[AllowAnonymous]</c>.
    /// </para>
    /// <para>
    /// OpenAPI is intentionally not registered here: the Api project calls
    /// <c>builder.Services.AddOpenApi(options =&gt; HostingExtensions.ConfigureOpenApi(options))</c> in its own <c>Program.cs</c>, because the XML comment
    /// generator of <c>Microsoft.AspNetCore.OpenApi</c> only processes calls made in the project whose comments it documents (ADR-0033).
    /// </para>
    /// </remarks>
    /// <param name="builder">The host builder of the API process.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static IHostApplicationBuilder AddAppApi(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddControllers()
            .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));

        // The OpenAPI generator reads the Minimal API JSON options; they must match the MVC options exactly.
        builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

        // AddOpenApi is called by the Api project itself (see ConfigureOpenApi): the XML comment generator
        // only handles calls made in the project whose documentation it describes (ADR-0033).

        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddScoped<ICurrentUser, HttpCurrentUser>();

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                builder.Configuration.GetSection("Authentication").Bind(options);
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ClockSkew = TokenClockSkew;
            });

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return builder;
    }

    /// <summary>
    /// Applies the OpenAPI settings shared by all services: OpenAPI 3.0 output and strongly typed IDs and single-value objects described
    /// as plain primitives, the <c>code</c> and <c>traceId</c> members of problem responses, an <c>operationId</c> per operation
    /// (<c>{Controller}_{Action}</c>), the <c>Bearer</c> security scheme and <c>x-abstract</c> on abstract (polymorphic) types
    /// (ADR-0015, ADR-0019, ADR-0023, ADR-0039).
    /// </summary>
    /// <remarks>
    /// Pass it to <c>AddOpenApi</c> in the Api project's <c>Program.cs</c>. The generated document is written to <c>openapi/{Project}.json</c> on build,
    /// committed, and used to generate clients (Refitter, Angular, Android, iOS); review its diff like code.
    /// </remarks>
    /// <param name="options">The OpenAPI options being configured; modified in place.</param>
    public static void ConfigureOpenApi(OpenApiOptions options)
    {
        options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
        options.AddSchemaTransformer<OpenApiSingleValueObjectSchemaTransformer>();
        options.AddSchemaTransformer<OpenApiProblemDetailsSchemaTransformer>();
        options.AddSchemaTransformer<OpenApiAbstractTypeSchemaTransformer>();
        options.AddOperationTransformer<OpenApiOperationIdTransformer>();
        options.AddDocumentTransformer<OpenApiBearerSecurityTransformer>();
        options.AddOperationTransformer<OpenApiBearerSecurityTransformer>();
    }

    /// <summary>
    /// Defines the JSON contract of all services: IDs and single-value objects as their primitive value, enums only as their names
    /// (numeric values such as <c>7</c> are rejected with HTTP 400, so undefined enum values never reach the domain),
    /// and numbers only as JSON numbers (never quoted strings).
    /// </summary>
    /// <remarks>
    /// Applied to MVC and to the OpenAPI generator by <see cref="AddAppApi"/>; both must use identical settings or the published contract
    /// would not match real responses. Call it yourself only when serializing the same types elsewhere (for example in tests).
    /// </remarks>
    /// <param name="options">The serializer options being configured; modified in place.</param>
    public static void ConfigureJson(System.Text.Json.JsonSerializerOptions options)
    {
        options.Converters.Add(new SingleValueObjectJsonConverterFactory());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.NumberHandling = JsonNumberHandling.Strict;
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
            .WithAddedModifier(PolymorphicDiscriminatorProperty.RemoveDuplicate);
    }

    /// <summary>
    /// Configures a Worker process: commands sent by message consumers run as the system identity, which is authenticated and has every scope.
    /// </summary>
    /// <remarks>
    /// Authorization of integration events happens at the boundary (only trusted services can publish to the broker), so requests created
    /// by consumers are not limited by <c>RequiresScopeAttribute</c>. An <see cref="ICurrentUser"/> registered earlier is not replaced.
    /// </remarks>
    /// <param name="builder">The host builder of the Worker process.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static IHostApplicationBuilder AddAppWorker(this IHostApplicationBuilder builder)
    {
        builder.Services.TryAddScoped<ICurrentUser, SystemCurrentUser>();
        return builder;
    }

    /// <summary>
    /// Maps the anonymous health endpoints used by Kubernetes and monitoring (ADR-0018): <c>/health/startup</c>, <c>/health/ready</c>,
    /// <c>/health/live</c> and <c>/health/dependencies</c>.
    /// </summary>
    /// <remarks>See <see cref="HealthTags"/> for what each endpoint checks and why dependencies are not part of readiness.</remarks>
    /// <param name="app">The built web application.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static WebApplication MapAppDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/startup", Probe(HealthTags.Startup)).AllowAnonymous();
        app.MapHealthChecks("/health/ready", Probe(HealthTags.Ready)).AllowAnonymous();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/dependencies", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Dependencies) || registration.Tags.Contains(HealthTags.MassTransit),
        }).AllowAnonymous();

        return app;
    }

    private static HealthCheckOptions Probe(string tag) => new() { Predicate = registration => registration.Tags.Contains(tag) };

    private static void AddAppTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddSource("MassTransit", ApplicationTelemetry.SourceName, InfrastructureTelemetry.SourceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("MassTransit", InfrastructureTelemetry.SourceName))
            .WithLogging();

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }
    }
}
