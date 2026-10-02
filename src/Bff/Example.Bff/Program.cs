using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Http.Downstream;
using SuperApp.Framework.Infrastructure.Http.UserContext;
using SuperApp.Framework.Infrastructure.Security;
using Example.Bff.Clients.Knowledge;
using Example.Bff.Clients.SleepDiary;
using Example.Bff.Hosting;
using Example.Bff.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// BFF of the Example experience (ADR-0038): the only entry point of the experience behind the shared edge gateway.
//   - public API /v1/... for the module (pass-through to the domain services and composed endpoints), contract Example.Bff_public.json;
//   - internal API /internal/v1/... for BFFs of other experiences, contract Example.Bff_internal.json (ADR-0039);
//   - every downstream call carries the user's JWT unchanged (ADR-0040); which components may connect is decided by NetworkPolicy (ADR-0041).
var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults("example-bff");
builder.AddAppApi();
builder.Services.AddOpenApi(BffOpenApiDocuments.Public, options => BffOpenApiDocuments.Configure(options, internalApi: false));
builder.Services.AddOpenApi(BffOpenApiDocuments.Internal, options => BffOpenApiDocuments.Configure(options, internalApi: true));
// Validation of content belongs to the domain services: the BFF does not evaluate validation attributes generated from the contract
// (they would reject valid input, e.g. nullable fields, before the service sees it) and relays the service's 400 with its codes.
// Input that cannot be bound at all (an unknown enum value in the query, a malformed body) is still rejected with 400 by the BFF.
builder.Services.Configure<MvcOptions>(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    options.ModelValidatorProviders.Clear();
    options.Filters.Add<InternalApiCallAudit>();
});
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ScopeAuthorizationResultHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ExampleBffScopes.InternalRead, policy => policy.RequireScope(ExampleBffScopes.InternalRead));

builder.Services.AddDownstreamApi<IKnowledgeApi>(builder.Configuration, "Knowledge").AddUserTokenForwarding();
builder.Services.AddDownstreamApi<ISleepDiaryApi>(builder.Configuration, "SleepDiary").AddUserTokenForwarding();

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
/// Entry point of the BFF of the Example experience (<c>example-bff</c>): MVC controllers over Refit clients of the domain services,
/// JWT validation, two OpenAPI documents and health endpoints.
/// </summary>
/// <remarks>
/// Declared explicitly as <see langword="public"/> <see langword="partial"/> so that tests can start the BFF with
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </remarks>
public partial class Program;
