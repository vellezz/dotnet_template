using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SuperApp.Framework.Infrastructure.Hosting;
using Microsoft.AspNetCore.OpenApi;

namespace ExperienceName.Bff.Hosting;

/// <summary>
/// The two OpenAPI documents of the BFF (ADR-0039): <c>public</c> for the module, <c>internal</c> for BFFs of other experiences.
/// </summary>
/// <remarks>
/// <para>
/// The build-time generator writes them to <c>openapi/ExperienceName.Bff_public.json</c> and <c>openapi/ExperienceName.Bff_internal.json</c>; both are
/// committed and reviewed like code. The split is by path: everything under <c>internal/</c> belongs to the internal document, everything
/// else to the public one.
/// </para>
/// <para>
/// Schemas of types generated from a service contract get the service name as prefix (<c>KnowledgeCategoryDto</c>,
/// <c>SleepDiaryCreatedResponse</c>), so equal type names of different services (<c>CreatedResponse</c>) do not collide in one document.
/// </para>
/// </remarks>
internal static class BffOpenApiDocuments
{
    /// <summary>Name of the public document.</summary>
    public const string Public = "public";

    /// <summary>Name of the internal document.</summary>
    public const string Internal = "internal";

    private const string ClientsNamespace = "ExperienceName.Bff.Clients.";

    /// <summary>Configures one of the documents.</summary>
    /// <param name="options">The options of the document being configured.</param>
    /// <param name="internalApi"><see langword="true"/> for the internal document, <see langword="false"/> for the public one.</param>
    public static void Configure(OpenApiOptions options, bool internalApi)
    {
        HostingExtensions.ConfigureOpenApi(options);
        options.ShouldInclude = description =>
            (description.RelativePath ?? string.Empty).StartsWith("internal/", StringComparison.Ordinal) == internalApi;
        options.CreateSchemaReferenceId = SchemaReferenceId;
    }

    private static string? SchemaReferenceId(JsonTypeInfo typeInfo)
    {
        var id = OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo);
        var type = typeInfo.Type;
        var ns = type.Namespace;
        if (id is null || ns is null || !ns.StartsWith(ClientsNamespace, StringComparison.Ordinal))
        {
            return id;
        }

        // A variant of a polymorphic type is named by ASP.NET Core as {base id}{variant id}; generated variant classes already start with
        // the base name (ContentBlockDtoHeadingBlockDto), so only the rest is returned: KnowledgeContentBlockDto + HeadingBlockDto.
        if (type.BaseType is { } baseType
            && baseType.IsDefined(typeof(JsonPolymorphicAttribute), inherit: false)
            && type.Name.StartsWith(baseType.Name, StringComparison.Ordinal))
        {
            return type.Name[baseType.Name.Length..];
        }

        return ns[ClientsNamespace.Length..].Split('.')[0] + id;
    }
}
