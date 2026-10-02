using Knowledge.Domain.Materials.Content;

namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Callout block (discriminator <c>"type": "callout"</c>): a highlighted box, such as a tip or a warning, that contains other blocks.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "callout", "variant": "Warning", "title": "...", "blocks": [ ...blocks... ] }</c>.</para>
/// <para>
/// Allowed at the top level and inside <see cref="ToggleBlockDto"/>. Its <c>blocks</c> may contain every top-level block type except
/// another callout (callouts cannot be nested). Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "callout", "variant": "Tip", "blocks": [ { "type": "paragraph", "text": [ { "text": "Go to bed at the same time." } ] } ] }
/// </code>
/// </example>
/// <param name="Variant">
/// Meaning of the callout, which also decides how clients render it: <c>Info</c>, <c>Tip</c>, <c>Warning</c> or <c>Important</c>
/// (<see cref="CalloutVariant"/>, serialized as a string).
/// </param>
/// <param name="Blocks">Nested content blocks shown inside the box, in display order; at least one is required.</param>
/// <param name="Title">Optional heading of the box, at most 200 characters; <see langword="null"/> when there is none.</param>
public sealed record CalloutBlockDto(CalloutVariant Variant, IReadOnlyList<ContentBlockDto> Blocks, string? Title = null) : ContentBlockDto;
