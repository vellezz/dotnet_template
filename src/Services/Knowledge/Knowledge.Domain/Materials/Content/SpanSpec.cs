namespace Knowledge.Domain.Materials.Content;

/// <summary>
/// Input description of one span of formatted text inside a <see cref="BlockSpec"/>: a piece of text with uniform formatting and an
/// optional link. It is the untrusted, not yet validated form of a <see cref="TextSpan"/>.
/// </summary>
/// <remarks>
/// <para>
/// Formatted text is a flat sequence of spans rather than nested markup: <c>"Read the **guide**"</c> becomes two spans,
/// <c>"Read the "</c> without marks and <c>"guide"</c> with <see cref="TextMarks.Bold"/>. Spans are concatenated without separators, so
/// spaces belong inside the span text. Their order in <see cref="BlockSpec.Text"/> becomes <see cref="TextSpan.Position"/>.
/// </para>
/// <para>
/// Validated by <see cref="ContentBuilder.Build"/>: at most <see cref="ContentBuilder.MaxSpansPerBlock"/> spans per block; the text must be
/// non-empty (white space alone is accepted) and at most <see cref="ContentBuilder.MaxSpanLength"/> characters; a link must be an absolute
/// HTTPS URL or a <c>mailto:</c> address, which rules out <c>javascript:</c> and other unsafe schemes.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// SpanSpec[] text =
/// [
///     new("Contact "),
///     new("the editors", TextMarks.Italic, "mailto:editors@example.com"),
///     new("."),
/// ];
/// </code>
/// </example>
/// <param name="Text">Text of the span; not empty, at most <see cref="ContentBuilder.MaxSpanLength"/> characters, not trimmed.</param>
/// <param name="Marks">Formatting flags of the span; combine with <c>|</c>. Defaults to <see cref="TextMarks.None"/>.</param>
/// <param name="LinkHref">
/// Link target: an absolute HTTPS URL or a <c>mailto:</c> address, at most <see cref="Knowledge.Domain.Common.WebUrl.MaxLength"/> characters;
/// <see langword="null"/> when the span is not a link.
/// </param>
public sealed record SpanSpec(string Text, TextMarks Marks = TextMarks.None, string? LinkHref = null);
