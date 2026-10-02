namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// One item of a <see cref="ListBlockDto"/>, optionally with a nested sub-list. Not a block by itself, so it has no <c>type</c> property.
/// </summary>
/// <remarks>JSON shape: <c>{ "text": [ ...spans... ], "children": { "style": "Unordered", "items": [ ... ] } }</c>.</remarks>
/// <param name="Text">
/// Text of the item as formatted text; from 1 to <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxSpansPerBlock"/> spans.
/// </param>
/// <param name="Children">
/// Optional list nested under this item, with its own style and items; <see langword="null"/> when the item has no sub-list.
/// Nesting is limited to <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxListDepth"/> list levels in total.
/// </param>
public sealed record ListItemDto(IReadOnlyList<SpanDto> Text, ListBlockDto? Children = null);
