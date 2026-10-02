namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Timestamp block (discriminator <c>"type": "timestamp"</c>): a described point in the recording of a video or podcast material,
/// for example the start of a chapter. Clients can use it to seek the player.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "timestamp", "atSeconds": 754, "text": [ ...spans... ] }</c>.</para>
/// <para>
/// The position refers to the material's main media; the service does not check it against the media duration, and it also accepts
/// the block in articles. Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "timestamp", "atSeconds": 754, "text": [ { "text": "Chapter 3: caffeine" } ] }
/// </code>
/// </example>
/// <param name="AtSeconds">Position in the recording in whole seconds from its start; not negative.</param>
/// <param name="Text">
/// Spans describing the point; from 1 to <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxSpansPerBlock"/> spans.
/// </param>
public sealed record TimestampBlockDto(int AtSeconds, IReadOnlyList<SpanDto> Text) : ContentBlockDto;
