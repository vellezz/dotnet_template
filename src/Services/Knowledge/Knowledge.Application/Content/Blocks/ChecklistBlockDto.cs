namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Checklist block (discriminator <c>"type": "checklist"</c>): a list of items, each shown as done or not done.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "checklist", "items": [ { "text": [ ...spans... ], "isChecked": false } ] }</c>.</para>
/// <para>
/// The checked state is part of the authored content (the same for every reader), not a per-user progress marker.
/// Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>; items cannot be nested.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "checklist", "items": [
///     { "text": [ { "text": "Dim the lights" } ], "isChecked": true },
///     { "text": [ { "text": "Put the phone away" } ], "isChecked": false } ] }
/// </code>
/// </example>
/// <param name="Items">Items of the checklist in display order; at least one is required.</param>
public sealed record ChecklistBlockDto(IReadOnlyList<ChecklistItemDto> Items) : ContentBlockDto;
