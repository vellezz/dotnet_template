using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SuperApp.Framework.Infrastructure.OpenApi;

/// <summary>
/// Marks the schema of an abstract type with the <c>x-abstract: true</c> extension, so that client generators produce an abstract class too.
/// </summary>
/// <remarks>
/// <para>
/// Polymorphic DTOs of this system (for example <c>ContentBlockDto</c> of Knowledge, discriminator <c>type</c>) are abstract: only their
/// derived types exist on the wire. OpenAPI 3.0 cannot say that a schema is abstract, so generators (Refitter / NJsonSchema) produce a
/// concrete base class. Such a base is a problem for a BFF that exposes the generated types in its own contract: the OpenAPI generator of
/// ASP.NET Core describes the variants and the discriminator only for an <b>abstract</b> base, so the BFF contract would show a bare object.
/// </para>
/// <para>
/// <c>x-abstract</c> is the extension NJsonSchema reads to generate <c>abstract</c> classes. Other tools ignore it. A consequence for clients:
/// an answer with a variant unknown to the client (the service added a block type) cannot be deserialized until the client is regenerated;
/// adding a variant is therefore announced to consumers like any other contract change.
/// </para>
/// </remarks>
internal sealed class OpenApiAbstractTypeSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type.IsAbstract && !type.IsInterface && context.JsonPropertyInfo is null)
        {
            schema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            schema.Extensions["x-abstract"] = new JsonNodeExtension(JsonValue.Create(true));
        }

        return Task.CompletedTask;
    }
}
