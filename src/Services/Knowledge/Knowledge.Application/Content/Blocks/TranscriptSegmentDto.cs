namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// One segment of a <see cref="TranscriptBlockDto"/>: what was said from a given moment of the recording. Not a block by itself,
/// so it has no <c>type</c> property.
/// </summary>
/// <remarks>JSON shape: <c>{ "atSeconds": 12, "text": [ ...spans... ], "speaker": "Guest" }</c>.</remarks>
/// <param name="AtSeconds">Start of the segment in whole seconds from the beginning of the recording; not negative.</param>
/// <param name="Text">
/// Spans of the spoken text; from 1 to <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxSpansPerBlock"/> spans.
/// </param>
/// <param name="Speaker">Optional name of the speaker, at most 100 characters; <see langword="null"/> when not specified.</param>
public sealed record TranscriptSegmentDto(int AtSeconds, IReadOnlyList<SpanDto> Text, string? Speaker = null);
