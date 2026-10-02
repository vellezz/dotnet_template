namespace Knowledge.Domain.Materials.Content;

/// <summary>
/// Visual and semantic variant of a <see cref="BlockType.Callout"/> block; clients choose icon and color from it.
/// </summary>
/// <remarks>
/// Passed and stored as the member name in <see cref="BlockSpec.Variant"/> / <see cref="ContentBlock.Variant"/> (case-sensitive, e.g.
/// <c>"Warning"</c>); a database <c>CHECK</c> constraint allows only these names, so renaming or adding a member requires a migration.
/// </remarks>
public enum CalloutVariant
{
    /// <summary>Neutral additional information.</summary>
    Info,

    /// <summary>Practical tip or recommendation.</summary>
    Tip,

    /// <summary>Warning about a risk or a common mistake.</summary>
    Warning,

    /// <summary>Important information the reader must not miss.</summary>
    Important,
}
