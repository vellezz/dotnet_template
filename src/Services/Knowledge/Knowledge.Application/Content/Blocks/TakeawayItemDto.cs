namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// One takeaway of a <see cref="KeyTakeawaysBlockDto"/>. Not a block by itself, so it has no <c>type</c> property.
/// </summary>
/// <remarks>JSON shape: <c>{ "text": [ ...spans... ] }</c>.</remarks>
/// <param name="Text">
/// Spans of the takeaway; from 1 to <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxSpansPerBlock"/> spans.
/// </param>
public sealed record TakeawayItemDto(IReadOnlyList<SpanDto> Text);
