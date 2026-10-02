using Microsoft.CodeAnalysis;

namespace SuperApp.Analyzers;

/// <summary>
/// Symbol checks shared by the analyzers. Framework types are recognized by name and namespace, because the analyzers
/// cannot reference <c>SuperApp.Framework</c> (they target <c>netstandard2.0</c> and run inside the compiler).
/// </summary>
internal static class SymbolExtensions
{
    private const string ResultNamespace = "SuperApp.Framework.Domain.Results";

    private const string ValueObjectsNamespace = "SuperApp.Framework.Domain.ValueObjects";

    /// <summary>Tells whether the type is <c>SuperApp.Framework.Domain.Results.Result</c> or derives from it (e.g. <c>Result&lt;T&gt;</c>).</summary>
    /// <param name="type">The type to check; <see langword="null"/> gives <see langword="false"/>.</param>
    /// <returns><see langword="true"/> for <c>Result</c> and every type in its inheritance chain below it.</returns>
    public static bool IsResultType(this ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name == "Result" && current.ContainingNamespace?.ToDisplayString() == ResultNamespace)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tells whether the type implements <c>SuperApp.Framework.Domain.ValueObjects.ISingleValueObject&lt;TSelf, TValue&gt;</c>, directly or through
    /// another interface such as <c>IStronglyTypedId&lt;TSelf, TValue&gt;</c>.
    /// </summary>
    /// <param name="type">The type to check; <see langword="null"/> and type parameters give <see langword="false"/>.</param>
    /// <returns><see langword="true"/> for strongly typed IDs and single-value objects.</returns>
    public static bool IsSingleValueObject(this ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named || named.TypeKind == TypeKind.TypeParameter)
        {
            return false;
        }

        foreach (var @interface in named.AllInterfaces)
        {
            var definition = @interface.OriginalDefinition;
            if (definition.Name == "ISingleValueObject"
                && definition.Arity == 2
                && definition.ContainingNamespace?.ToDisplayString() == ValueObjectsNamespace)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tells whether the type is the given class or derives from it.</summary>
    /// <param name="type">The type to check; <see langword="null"/> gives <see langword="false"/>.</param>
    /// <param name="fullName">Full name of the base class as printed by <c>ToDisplayString()</c>, e.g. <c>Microsoft.EntityFrameworkCore.DbContext</c>.</param>
    /// <returns><see langword="true"/> when <paramref name="fullName"/> is the type itself or one of its base classes (interfaces are not checked).</returns>
    public static bool InheritsFrom(this INamedTypeSymbol? type, string fullName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == fullName)
            {
                return true;
            }
        }

        return false;
    }
}
