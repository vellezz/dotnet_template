namespace Knowledge.Domain.Materials.Content;

/// <summary>
/// One span of formatted text of a <see cref="ContentBlock"/>: text with uniform <see cref="Marks"/> and an optional link. Stored as a
/// row of the <c>ContentTextSpans</c> table, keyed by the owning block and <see cref="Position"/>.
/// </summary>
/// <remarks>
/// Immutable; created only by <see cref="ContentBuilder"/> from a validated <see cref="SpanSpec"/>. The text of a block is the concatenation
/// of its spans in <see cref="Position"/> order. A database <c>CHECK</c> constraint repeats the link rule (<c>https://</c> or <c>mailto:</c>).
/// </remarks>
public sealed class TextSpan
{
    private TextSpan(int position, string text, TextMarks marks, string? linkHref)
    {
        Position = position;
        Text = text;
        Marks = marks;
        LinkHref = linkHref;
    }

    /// <summary>Gets the zero-based position of the span within its block.</summary>
    public int Position { get; private set; }

    /// <summary>Gets the text of the span; non-empty, at most <see cref="ContentBuilder.MaxSpanLength"/> characters.</summary>
    public string Text { get; private set; }

    /// <summary>Gets the formatting flags of the span.</summary>
    public TextMarks Marks { get; private set; }

    /// <summary>Gets the link target (an HTTPS URL or a <c>mailto:</c> address), or <see langword="null"/> when the span is not a link.</summary>
    public string? LinkHref { get; private set; }

    /// <summary>Creates a span from an already validated specification; used only when <see cref="ContentBlock"/> nodes are created.</summary>
    /// <param name="position">Zero-based position of the span within its block.</param>
    /// <param name="spec">The validated span specification.</param>
    /// <returns>The new span.</returns>
    internal static TextSpan Create(int position, SpanSpec spec) => new(position, spec.Text, spec.Marks, spec.LinkHref);
}
