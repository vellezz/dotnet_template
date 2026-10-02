using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Messaging;
using ServiceName.Infrastructure;
using ServiceName.Infrastructure.Persistence.Write;

// Api process of the ServiceName service: serves the HTTP API behind the gateways (bff-web, gateway-mobile).
// It is the composition root: the only place where the Api project touches Infrastructure (architecture test 6).
// It never applies migrations; the startup probe fails until SuperApp.Migrator (dev/test) or the DBA (prod) has applied them (ADR-0004, ADR-0018).
var builder = WebApplication.CreateBuilder(args);

// Telemetry, clock, ProblemDetails and health checks; "servicename-api" is the service.name seen in Grafana.
builder.AddAppServiceDefaults<ServiceNameWriteDbContext>("servicename-api");
// Controllers, JWT bearer validation (Authentication section of appsettings) and ICurrentUser from the token (ADR-0007).
builder.AddAppApi();
// OpenAPI must be registered here, in the Api project, so that XML comments of the controllers reach the contract (ADR-0033).
builder.Services.AddOpenApi(options => HostingExtensions.ConfigureOpenApi(options));
// Api only stores integration events in the outbox; the Worker delivers them to RabbitMQ.
builder.Services.AddServiceNameInfrastructure(builder.Configuration, OutboxDelivery.Disabled);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
// The contract is public (no token) so that the gateway and client generators can read it.
app.MapOpenApi().AllowAnonymous();
// Health endpoints for the Kubernetes probes and monitoring (ADR-0018).
app.MapAppDefaultEndpoints();

await app.RunAsync();

/// <summary>Entry point of the Api process, declared explicitly so that integration tests can start it with <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
