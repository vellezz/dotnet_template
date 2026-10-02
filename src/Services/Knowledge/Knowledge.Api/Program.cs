using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Messaging;
using Knowledge.Infrastructure;
using Knowledge.Infrastructure.Persistence.Write;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults<KnowledgeWriteDbContext>("knowledge-api");
builder.AddAppApi();
builder.Services.AddOpenApi(options => HostingExtensions.ConfigureOpenApi(options));
builder.Services.AddKnowledgeInfrastructure(builder.Configuration, OutboxDelivery.Disabled);

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
/// Entry point of the Knowledge HTTP API (<c>knowledge-api</c>): thin MVC controllers over the MediatR pipeline, JWT validation,
/// OpenAPI document and health endpoints.
/// </summary>
/// <remarks>
/// The API writes integration events to the outbox but does not deliver them (<c>OutboxDelivery.Disabled</c>); delivery to RabbitMQ is
/// done by <c>Knowledge.Worker</c>. The class is declared explicitly as <see langword="public"/> <see langword="partial"/> so that
/// integration tests can start the API with <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </remarks>
public partial class Program;
