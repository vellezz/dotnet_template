using Knowledge.Domain.Materials.Content;

namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// List block (discriminator <c>"type": "list"</c>): a bulleted or numbered list whose items may contain nested lists.
/// </summary>
/// <remarks>
/// <para>
/// JSON shape: <c>{ "type": "list", "style": "Unordered", "items": [ { "text": [ ...spans... ], "children": { ...nested list... } } ] }</c>.
/// A nested list (<see cref="ListItemDto.Children"/>) is written without a <c>type</c> property.
/// </para>
/// <para>
/// Lists can be nested at most <see cref="ContentBuilder.MaxListDepth"/> levels deep, counting the outermost list. Allowed at the top
/// level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>. Rule violations are reported as
/// <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "list", "style": "Ordered", "items": [
///     { "text": [ { "text": "Prepare the bedroom" } ],
///       "children": { "style": "Unordered", "items": [ { "text": [ { "text": "Cool" } ] }, { "text": [ { "text": "Dark" } ] } ] } },
///     { "text": [ { "text": "Go to bed" } ] } ] }
/// </code>
/// </example>
/// <param name="Style">Kind of list: <c>Unordered</c> (bullets) or <c>Ordered</c> (numbers) (<see cref="ListStyle"/>, serialized as a string).</param>
/// <param name="Items">Items of the list in display order; at least one is required.</param>
public sealed record ListBlockDto(ListStyle Style, IReadOnlyList<ListItemDto> Items) : ContentBlockDto;
