using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Messaging;
using MassTransit;
using SleepDiary.Infrastructure;
using SleepDiary.Infrastructure.Persistence.Write;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults<SleepDiaryWriteDbContext>("sleepdiary-worker");
builder.AddAppWorker();
builder.Services.AddSleepDiaryInfrastructure(
    builder.Configuration,
    OutboxDelivery.Enabled,
    bus => bus.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();

app.MapAppDefaultEndpoints();

await app.RunAsync();

/// <summary>
/// Entry point of the SleepDiary Worker host: delivers integration events from the outbox to RabbitMQ (<see cref="OutboxDelivery.Enabled"/>)
/// and runs the MassTransit consumers of the service, as the system identity.
/// </summary>
/// <remarks>
/// Consumers are discovered in this assembly (<c>AddConsumers(typeof(Program).Assembly)</c>); none exist yet, because SleepDiary does not
/// subscribe to other contexts' events. Declared explicitly as a public partial class so that consumer registration and tests can reference it.
/// </remarks>
public partial class Program;
