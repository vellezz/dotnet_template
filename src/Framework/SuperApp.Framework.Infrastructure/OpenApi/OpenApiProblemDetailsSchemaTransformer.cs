using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SuperApp.Framework.Infrastructure.OpenApi;

/// <summary>
/// Adds the extension members every error response of this system carries, <c>code</c> and <c>traceId</c>, to the
/// <see cref="ProblemDetails"/> and <see cref="ValidationProblemDetails"/> schemas of the OpenAPI document (ADR-0015, ADR-0019).
/// </summary>
/// <remarks>
/// <c>ResultHttpExtensions</c> writes both members into every problem response, but they are dictionary extensions, invisible to the
/// schema generator. Without them in the contract, generated clients could not read <c>code</c>, the value clients are supposed to branch on.
/// </remarks>
internal sealed class OpenApiProblemDetailsSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (!typeof(ProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type))
        {
            return Task.CompletedTask;
        }

        schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
        schema.Properties["code"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Stable, machine-readable error code, e.g. knowledge.material.archived; clients branch on it (ADR-0015).",
        };
        schema.Properties["traceId"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Trace identifier of the request, used to find its traces and logs.",
        };
        return Task.CompletedTask;
    }
}
