namespace Knowledge.Domain.Materials.Content;

/// <summary>
/// Style of a <see cref="BlockType.List"/> block.
/// </summary>
/// <remarks>
/// Passed and stored as the member name in <see cref="BlockSpec.Variant"/> / <see cref="ContentBlock.Variant"/> (case-sensitive,
/// <c>"Unordered"</c> or <c>"Ordered"</c>); a database <c>CHECK</c> constraint allows only these names. Each nested list has its own style.
/// </remarks>
public enum ListStyle
{
    /// <summary>Bulleted list.</summary>
    Unordered,

    /// <summary>Numbered list.</summary>
    Ordered,
}
