using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Messaging;
using MassTransit;
using ServiceName.Infrastructure;
using ServiceName.Infrastructure.Persistence.Write;

// Worker process of the ServiceName service: consumes integration events from RabbitMQ and delivers the service's outbox.
// It has no public API; it exposes only the health endpoints. Scaled by KEDA on queue length.
// Like the Api, it never applies migrations; it waits in the startup probe until they are applied (ADR-0004, ADR-0018).
var builder = WebApplication.CreateBuilder(args);

// Telemetry, clock, ProblemDetails and health checks; "servicename-worker" is the service.name seen in Grafana.
builder.AddAppServiceDefaults<ServiceNameWriteDbContext>("servicename-worker");
// ICurrentUser for consumers: the system identity (authenticated, every scope) instead of a user token.
builder.AddAppWorker();
// The Worker delivers outbox messages to RabbitMQ and registers every consumer found in this assembly (see Consumers/).
builder.Services.AddServiceNameInfrastructure(
    builder.Configuration,
    OutboxDelivery.Enabled,
    bus => bus.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();

// Health endpoints for the Kubernetes probes and monitoring (ADR-0018).
app.MapAppDefaultEndpoints();

await app.RunAsync();

/// <summary>
/// Entry point of the Worker process (event consumers, outbox delivery); declared explicitly so that <c>typeof(Program).Assembly</c>
/// can be scanned for consumers and tests can start the process.
/// </summary>
public partial class Program;
