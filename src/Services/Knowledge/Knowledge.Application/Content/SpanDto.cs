using Knowledge.Domain.Materials.Content;

namespace Knowledge.Application.Content;

/// <summary>
/// A span of formatted text: a run of characters that share the same formatting and, optionally, the same link.
/// Every text block (paragraph, quote, list item, table cell and so on) holds its text as an ordered list of spans.
/// </summary>
/// <remarks>
/// <para>
/// JSON shape: <c>{ "text": "...", "marks": [ "Bold", "Italic" ], "link": "https://..." }</c>. To format part of a sentence, split it
/// into several spans; concatenating the <c>text</c> of all spans gives the plain text of the block.
/// </para>
/// <para>
/// A block may hold at most <see cref="ContentBuilder.MaxSpansPerBlock"/> spans. Rule violations are reported as
/// <c>knowledge.content.invalid_block</c> (HTTP 400) for the block that contains the span.
/// </para>
/// <para>
/// On reads, <c>marks</c> is <see langword="null"/> (not an empty list) for unformatted text and otherwise lists the flags in the order
/// they are declared in <see cref="TextMarks"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [ { "text": "Contact " }, { "text": "the sleep clinic", "marks": [ "Bold" ], "link": "mailto:clinic@example.com" } ]
/// </code>
/// </example>
/// <param name="Text">The characters of the span. Required, must not be empty, at most <see cref="ContentBuilder.MaxSpanLength"/> characters.</param>
/// <param name="Marks">
/// Optional formatting as a list of single <see cref="TextMarks"/> flags serialized as strings: <c>Bold</c>, <c>Italic</c>,
/// <c>Underline</c>, <c>Strikethrough</c>, <c>Code</c>, <c>Highlight</c>. Do not send <c>None</c>. <see langword="null"/> or an empty
/// list means unformatted text; duplicates are harmless because the flags are combined.
/// </param>
/// <param name="Link">
/// Optional link target: an absolute <c>https</c> URL or a <c>mailto:</c> address, at most
/// <see cref="Knowledge.Domain.Common.WebUrl.MaxLength"/> characters. <see langword="null"/> when the span is not a link.
/// </param>
public sealed record SpanDto(string Text, IReadOnlyList<TextMarks>? Marks = null, string? Link = null);
