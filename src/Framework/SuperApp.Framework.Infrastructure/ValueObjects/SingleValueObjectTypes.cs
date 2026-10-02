using SuperApp.Framework.Domain.ValueObjects;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SuperApp.Framework.Infrastructure.ValueObjects;

/// <summary>
/// Discovers strongly typed IDs and single-value objects by reflection; shared by the EF Core, JSON and OpenAPI integrations (ADR-0023, ADR-0024).
/// </summary>
/// <remarks>
/// A type qualifies when it is a struct implementing <see cref="ISingleValueObject{TSelf, TValue}"/> (directly or through
/// <see cref="IStronglyTypedId{TSelf, TValue}"/>) with itself as <c>TSelf</c>. Service code normally has no reason to call it.
/// </remarks>
public static class SingleValueObjectTypes
{
    private static readonly ConcurrentDictionary<Type, Type?> ValueTypes = new();

    /// <summary>
    /// Checks whether <paramref name="type"/> is a struct implementing <see cref="ISingleValueObject{TSelf, TValue}"/> for itself; results are cached per type.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <param name="valueType">The wrapped primitive type (<c>TValue</c>), or <see langword="null"/> when <paramref name="type"/> is not a single-value object.</param>
    /// <returns><see langword="true"/> when the type is a strongly typed ID or a single-value object.</returns>
    public static bool TryGetValueType(Type type, [NotNullWhen(true)] out Type? valueType)
    {
        valueType = ValueTypes.GetOrAdd(type, static candidate =>
            candidate.IsValueType
                ? candidate.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISingleValueObject<,>) && i.GenericTypeArguments[0] == candidate)
                    .Select(i => i.GenericTypeArguments[1])
                    .FirstOrDefault()
                : null);

        return valueType is not null;
    }

    /// <summary>Finds all strongly typed IDs and single-value objects declared in <paramref name="assemblies"/>.</summary>
    /// <param name="assemblies">Assemblies to scan, typically the service's Domain assembly.</param>
    /// <returns>A lazily evaluated sequence of value object types with their primitive value types.</returns>
    public static IEnumerable<(Type Type, Type ValueType)> Find(IEnumerable<Assembly> assemblies) =>
        assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => (Type: type, Found: TryGetValueType(type, out var valueType), ValueType: valueType))
            .Where(candidate => candidate.Found)
            .Select(candidate => (candidate.Type, candidate.ValueType!));
}
