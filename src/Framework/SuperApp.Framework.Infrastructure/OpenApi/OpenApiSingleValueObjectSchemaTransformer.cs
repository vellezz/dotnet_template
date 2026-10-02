using SuperApp.Framework.Infrastructure.ValueObjects;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SuperApp.Framework.Infrastructure.OpenApi;

/// <summary>
/// Describes strongly typed IDs and single-value objects in the OpenAPI document as the primitive they serialize to
/// (for example <c>string</c>/<c>uuid</c> for <c>MaterialId</c>) instead of an object with a <c>Value</c> property (ADR-0019, ADR-0023).
/// </summary>
internal sealed class OpenApiSingleValueObjectSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (!SingleValueObjectTypes.TryGetValueType(type, out var valueType))
        {
            return Task.CompletedTask;
        }

        (schema.Type, schema.Format) = valueType switch
        {
            _ when valueType == typeof(Guid) => (JsonSchemaType.String, "uuid"),
            _ when valueType == typeof(int) => (JsonSchemaType.Integer, "int32"),
            _ when valueType == typeof(long) => (JsonSchemaType.Integer, "int64"),
            _ when valueType == typeof(decimal) => (JsonSchemaType.Number, "double"),
            _ => (JsonSchemaType.String, null),
        };
        schema.Properties?.Clear();
        schema.Required?.Clear();
        return Task.CompletedTask;
    }
}
