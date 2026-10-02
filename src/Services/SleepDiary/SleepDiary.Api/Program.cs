using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Messaging;
using SleepDiary.Infrastructure;
using SleepDiary.Infrastructure.Persistence.Write;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults<SleepDiaryWriteDbContext>("sleepdiary-api");
builder.AddAppApi();
builder.Services.AddOpenApi(options => HostingExtensions.ConfigureOpenApi(options));
builder.Services.AddSleepDiaryInfrastructure(builder.Configuration, OutboxDelivery.Disabled);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapOpenApi().AllowAnonymous();
app.MapAppDefaultEndpoints();

await app.RunAsync();

/// <summary>
/// Entry point of the SleepDiary HTTP API host (controllers, OpenAPI, health endpoints). Integration events are only written to the outbox
/// here (<see cref="OutboxDelivery.Disabled"/>); the Worker delivers them.
/// </summary>
/// <remarks>Declared explicitly as a public partial class so that integration tests can start the API with <c>WebApplicationFactory&lt;Program&gt;</c>.</remarks>
public partial class Program;
