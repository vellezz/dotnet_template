using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Messaging;
using MassTransit;
using Knowledge.Infrastructure;
using Knowledge.Infrastructure.Persistence.Write;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults<KnowledgeWriteDbContext>("knowledge-worker");
builder.AddAppWorker();
builder.Services.AddKnowledgeInfrastructure(
    builder.Configuration,
    OutboxDelivery.Enabled,
    bus => bus.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();

app.MapAppDefaultEndpoints();

await app.RunAsync();

/// <summary>
/// Entry point of the Knowledge background process (<c>knowledge-worker</c>): hosts the MassTransit consumers of this assembly and
/// delivers the outbox of the Knowledge service to RabbitMQ.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the API, the worker enables outbox delivery (<c>OutboxDelivery.Enabled</c>), so integration events written by both processes
/// are sent from here. Commands sent by consumers run as the system user (<c>AddAppWorker</c>), which has every scope and no subject.
/// The process exposes only the health endpoints.
/// </para>
/// <para>
/// The class is declared explicitly so that tests can reference the assembly and consumers are discovered with
/// <c>AddConsumers(typeof(Program).Assembly)</c>.
/// </para>
/// </remarks>
public partial class Program;
