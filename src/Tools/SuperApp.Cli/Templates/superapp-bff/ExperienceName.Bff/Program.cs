using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Security;
using ExperienceName.Bff.Hosting;
using ExperienceName.Bff.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// BFF of the ExperienceName experience (ADR-0038): the only entry point of the experience behind the shared edge gateway.
//   - public API /v1/... for the module, contract openapi/ExperienceName.Bff_public.json;
//   - internal API /internal/v1/... for BFFs of other experiences, contract openapi/ExperienceName.Bff_internal.json (ADR-0039);
//   - every downstream call carries the user's JWT unchanged (ADR-0040); which components may connect is decided by NetworkPolicy (ADR-0041).
//
// Adding a domain service of the experience (see docs/przewodnik, recipe for a new experience):
//   1. Clients/{Service}/{service}.refitter pointing at the service's committed contract; run `dotnet refitter --settings-file ...`
//      (generated code goes to Clients/{Service}/Generated and is committed);
//   2. "Downstream": { "{Service}": { "BaseAddress": "..." } } in appsettings.Development.json and in the Helm values (downstream);
//   3. builder.Services.AddDownstreamApi<I{Service}Api>(builder.Configuration, "{Service}").AddUserTokenForwarding();
//   4. controllers under Controllers/ relaying answers with this.ToActionResult(await client.XAsync(...));
//   5. endpoints composed from several services fetch their parts with PartialResponseFetcher.FetchAsync (partial rendering).
var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults("experiencename-bff");
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
    .AddPolicy(ExperienceNameBffScopes.InternalRead, policy => policy.RequireScope(ExperienceNameBffScopes.InternalRead));

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
/// Entry point of the BFF of the ExperienceName experience (<c>experiencename-bff</c>): MVC controllers over Refit clients of the domain
/// services, JWT validation, two OpenAPI documents and health endpoints.
/// </summary>
/// <remarks>
/// Declared explicitly as <see langword="public"/> <see langword="partial"/> so that tests can start the BFF with
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </remarks>
public partial class Program;
