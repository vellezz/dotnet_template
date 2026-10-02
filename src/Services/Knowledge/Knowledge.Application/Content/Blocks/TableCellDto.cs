namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// One cell of a <see cref="TableRowDto"/>. Not a block by itself, so it has no <c>type</c> property.
/// </summary>
/// <remarks>JSON shape: <c>{ "text": [ ...spans... ] }</c>; <c>{ "text": [] }</c> is an empty cell.</remarks>
/// <param name="Text">
/// Spans of the cell; the list may be empty (an empty cell, the only text item where that is allowed) and holds at most
/// <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxSpansPerBlock"/> spans.
/// </param>
public sealed record TableCellDto(IReadOnlyList<SpanDto> Text);
