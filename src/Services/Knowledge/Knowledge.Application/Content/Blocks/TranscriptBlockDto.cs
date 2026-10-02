namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Transcript block (discriminator <c>"type": "transcript"</c>): the spoken text of a recording, split into timed segments.
/// </summary>
/// <remarks>
/// <para>
/// JSON shape: <c>{ "type": "transcript", "segments": [ { "atSeconds": 0, "text": [ ...spans... ], "speaker": "..." } ] }</c>.
/// Segments are not blocks and have no <c>type</c> property.
/// </para>
/// <para>
/// The service does not require segments to be sorted by time; they are returned in the order they were sent. Allowed at the top level
/// and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>. Rule violations are reported as
/// <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "transcript", "segments": [
///     { "atSeconds": 0, "speaker": "Host", "text": [ { "text": "Welcome to the show." } ] },
///     { "atSeconds": 12, "speaker": "Guest", "text": [ { "text": "Thanks for having me." } ] } ] }
/// </code>
/// </example>
/// <param name="Segments">Segments of the transcript in display order; at least one is required.</param>
public sealed record TranscriptBlockDto(IReadOnlyList<TranscriptSegmentDto> Segments) : ContentBlockDto;
