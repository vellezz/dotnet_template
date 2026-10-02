namespace Knowledge.Domain.Materials.Content;

/// <summary>
/// Formatting of a span of text (<see cref="SpanSpec"/>, <see cref="TextSpan"/>). A flags enum: marks are combined with <c>|</c>,
/// e.g. <c>TextMarks.Bold | TextMarks.Italic</c>.
/// </summary>
/// <remarks>
/// A link is not a mark: a span is a link when its <see cref="TextSpan.LinkHref"/> is not <see langword="null"/>, and it can carry any
/// marks as well. The value is stored as an integer, so the numeric values must never change. <see cref="ContentBuilder"/> rejects
/// values with undefined bits (e.g. <c>(TextMarks)64</c>), so stored content only ever contains the flags declared here; adding a flag
/// is a deliberate change of this enum.
/// </remarks>
[Flags]
public enum TextMarks
{
    /// <summary>No formatting.</summary>
    None = 0,

    /// <summary>Bold text.</summary>
    Bold = 1,

    /// <summary>Italic text.</summary>
    Italic = 2,

    /// <summary>Underlined text.</summary>
    Underline = 4,

    /// <summary>Struck-through text.</summary>
    Strikethrough = 8,

    /// <summary>Inline code, displayed in a monospaced font.</summary>
    Code = 16,

    /// <summary>Highlighted text (marked with a background color).</summary>
    Highlight = 32,
}
