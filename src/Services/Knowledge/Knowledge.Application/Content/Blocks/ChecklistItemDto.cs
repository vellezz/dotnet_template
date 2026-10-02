namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// One item of a <see cref="ChecklistBlockDto"/>. Not a block by itself, so it has no <c>type</c> property.
/// </summary>
/// <remarks>JSON shape: <c>{ "text": [ ...spans... ], "isChecked": true }</c>.</remarks>
/// <param name="Text">
/// Text of the item as formatted text; from 1 to <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxSpansPerBlock"/> spans.
/// </param>
/// <param name="IsChecked"><see langword="true"/> when the item is shown as done.</param>
public sealed record ChecklistItemDto(IReadOnlyList<SpanDto> Text, bool IsChecked);
