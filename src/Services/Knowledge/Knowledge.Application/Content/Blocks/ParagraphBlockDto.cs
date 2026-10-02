namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Paragraph block (discriminator <c>"type": "paragraph"</c>): the basic block of formatted running text.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "paragraph", "text": [ { "text": "...", "marks": [ "Bold" ], "link": "https://..." } ] }</c>.</para>
/// <para>
/// Formatting (bold, links and so on) is expressed by splitting the text into <see cref="SpanDto"/> items. Allowed at the top level
/// and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>. Rule violations are reported as
/// <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "paragraph", "text": [ { "text": "Read the " }, { "text": "full study", "link": "https://www.example.org/study" }, { "text": "." } ] }
/// </code>
/// </example>
/// <param name="Text">
/// Spans of the paragraph in reading order; from 1 to <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxSpansPerBlock"/> spans.
/// </param>
public sealed record ParagraphBlockDto(IReadOnlyList<SpanDto> Text) : ContentBlockDto;
