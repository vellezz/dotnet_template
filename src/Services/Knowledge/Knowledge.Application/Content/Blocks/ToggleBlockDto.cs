namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Toggle block (discriminator <c>"type": "toggle"</c>): a title that expands to show nested blocks, for example an FAQ entry.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "toggle", "title": "...", "blocks": [ ...blocks... ] }</c>.</para>
/// <para>
/// Its <c>blocks</c> may contain every top-level block type, including callouts and other toggles, but toggles can be nested at most
/// <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxToggleDepth"/> levels deep. Allowed at the top level and inside
/// <see cref="CalloutBlockDto"/>. Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "toggle", "title": "Is napping bad?", "blocks": [ { "type": "paragraph", "text": [ { "text": "Short naps are fine." } ] } ] }
/// </code>
/// </example>
/// <param name="Title">Title visible while the toggle is collapsed. Required, must not be blank, at most 200 characters.</param>
/// <param name="Blocks">Nested content blocks shown when the toggle is expanded, in display order; at least one is required.</param>
public sealed record ToggleBlockDto(string Title, IReadOnlyList<ContentBlockDto> Blocks) : ContentBlockDto;
