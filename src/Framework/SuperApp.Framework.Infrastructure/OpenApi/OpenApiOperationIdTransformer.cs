using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SuperApp.Framework.Infrastructure.OpenApi;

/// <summary>
/// Gives every controller operation a stable <c>operationId</c> of the form <c>{Controller}_{Action}</c>, e.g. <c>Categories_Create</c>.
/// </summary>
/// <remarks>
/// <para>
/// Client generators (Refitter for .NET, the Angular, Kotlin and Swift generators) name the generated methods after the
/// <c>operationId</c>; without it they invent names from the path and the HTTP method, which change whenever a route changes. The identifier is
/// derived from code that already has to be unique: an action name within its controller, and a controller name within an API.
/// </para>
/// <para>
/// Renaming a controller or an action therefore renames the generated client method: treat it like any other contract change and look at
/// the diff of the committed OpenAPI document. An <c>operationId</c> set explicitly (for example with <c>[EndpointName]</c>) is kept.
/// </para>
/// </remarks>
internal sealed class OpenApiOperationIdTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.OperationId) && context.Description.ActionDescriptor is ControllerActionDescriptor action)
        {
            operation.OperationId = $"{action.ControllerName}_{action.ActionName}";
        }

        return Task.CompletedTask;
    }
}
