namespace Knowledge.Infrastructure.Persistence.Read.Models;

/// <summary>
/// Read model of one row of <c>knowledge.ContentTextSpans</c>: a fragment of formatted text inside a content block (paragraph, list item,
/// table cell…) with uniform formatting.
/// </summary>
internal sealed class ContentTextSpanRow
{
    /// <summary>Identifier of the block that contains the span (first part of the key).</summary>
    public Guid BlockId { get; init; }

    /// <summary>Zero-based position of the span within the block (second part of the key).</summary>
    public int Position { get; init; }

    /// <summary>The text of the span.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Formatting as the integer value of the <c>TextMarks</c> flags enum (bold, italic, code…); 0 means no formatting.</summary>
    public int Marks { get; init; }

    /// <summary>Link target (<c>https:</c> or <c>mailto:</c>) when the span is a link; otherwise <see langword="null"/>.</summary>
    public string? LinkHref { get; init; }
}
