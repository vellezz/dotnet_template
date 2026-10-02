using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SuperApp.Framework.Infrastructure.OpenApi;

/// <summary>
/// Declares in the OpenAPI document that the API requires a bearer JWT: a <c>Bearer</c> security scheme and a security requirement on every
/// operation that is not anonymous.
/// </summary>
/// <remarks>
/// <para>
/// Every API of this system validates a JWT access token issued by the CIAM (ADR-0007, ADR-0040); the fallback authorization policy rejects
/// anonymous requests. Without the scheme in the contract, generated clients and tools reading the document do not know that a token is
/// needed. Operations whose endpoint carries <see cref="IAllowAnonymous"/> metadata (<c>[AllowAnonymous]</c>) get no requirement.
/// </para>
/// <para>
/// The scheme does not say how to obtain the token: a module gets it from the shell or the edge gateway, a BFF passes the user's token on,
/// and system callers use client credentials. It is a description of the HTTP contract, not of the login flow.
/// </para>
/// </remarks>
internal sealed class OpenApiBearerSecurityTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    private const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT access token issued by the CIAM, passed unchanged by the edge gateway and BFF.",
        };
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
        });
        return Task.CompletedTask;
    }
}
