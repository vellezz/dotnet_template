namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Key takeaways block (discriminator <c>"type": "keyTakeaways"</c>): a summary of the most important points of the material.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "keyTakeaways", "items": [ { "text": [ ...spans... ] } ] }</c>.</para>
/// <para>
/// Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>; items cannot be nested.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "keyTakeaways", "items": [ { "text": [ { "text": "Adults need 7-9 hours of sleep." } ] } ] }
/// </code>
/// </example>
/// <param name="Items">The takeaways in display order; at least one is required.</param>
public sealed record KeyTakeawaysBlockDto(IReadOnlyList<TakeawayItemDto> Items) : ContentBlockDto;
