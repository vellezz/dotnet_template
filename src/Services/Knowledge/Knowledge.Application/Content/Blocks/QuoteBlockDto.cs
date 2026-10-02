namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Quote block (discriminator <c>"type": "quote"</c>): a highlighted quotation with an optional author and source.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "quote", "text": [ ...spans... ], "author": "...", "source": "..." }</c>.</para>
/// <para>
/// Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "quote", "text": [ { "text": "A good laugh and a long sleep are the best cures in the doctor's book." } ], "author": "Irish proverb" }
/// </code>
/// </example>
/// <param name="Text">
/// Spans of the quotation; from 1 to <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxSpansPerBlock"/> spans.
/// </param>
/// <param name="Author">Optional author of the quotation, at most 200 characters; <see langword="null"/> when unknown.</param>
/// <param name="Source">Optional source, for example the title of a book, at most 200 characters; <see langword="null"/> when there is none.</param>
public sealed record QuoteBlockDto(IReadOnlyList<SpanDto> Text, string? Author = null, string? Source = null) : ContentBlockDto;
