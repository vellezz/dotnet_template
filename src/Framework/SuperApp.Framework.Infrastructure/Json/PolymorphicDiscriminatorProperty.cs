using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SuperApp.Framework.Infrastructure.Json;

/// <summary>
/// <c>System.Text.Json</c> contract modifier that drops a property duplicating the type discriminator of a polymorphic base type.
/// </summary>
/// <remarks>
/// <para>
/// The OpenAPI contracts of this system describe polymorphic types (for example the content blocks of Knowledge, discriminator <c>type</c>)
/// with the discriminator also listed as a property of every derived schema. Client generators (Refitter) therefore generate both
/// <c>[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]</c> on the base class and a <c>Type</c> property on each derived class.
/// <c>System.Text.Json</c> refuses such a type (<c>InvalidOperationException</c>: property conflicts with an existing metadata property name)
/// when it serializes, deserializes or exports its JSON schema.
/// </para>
/// <para>
/// The duplicate carries no information: the discriminator value is written and read by the polymorphism support. This modifier removes the
/// property from the serialization contract, so generated code can be used as is and regenerated at any time. It is applied to the JSON
/// options of every API (<c>HostingExtensions.ConfigureJson</c>) and of downstream clients (<c>AddDownstreamApi</c>).
/// </para>
/// </remarks>
public static class PolymorphicDiscriminatorProperty
{
    /// <summary>Removes, from the contract of a derived type, the property whose JSON name equals the discriminator of a polymorphic base type.</summary>
    /// <param name="typeInfo">The contract being built; modified in place.</param>
    public static void RemoveDuplicate(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var baseType = typeInfo.Type.BaseType; baseType is not null && baseType != typeof(object); baseType = baseType.BaseType)
        {
            if (baseType.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false) is { TypeDiscriminatorPropertyName: { } discriminator })
            {
                var duplicate = typeInfo.Properties.FirstOrDefault(property => property.Name == discriminator);
                if (duplicate is not null)
                {
                    typeInfo.Properties.Remove(duplicate);
                }

                return;
            }
        }
    }
}
