namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Heading block (discriminator <c>"type": "heading"</c>): the title of a section of the material content.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "heading", "level": 2, "text": "..." }</c>.</para>
/// <para>
/// Unlike most text blocks the heading is plain text without formatting spans. Clients can build a table of contents from headings.
/// Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "heading", "level": 2, "text": "Why sleep matters" }
/// </code>
/// </example>
/// <param name="Level">Level of the heading, from 1 (most important) to 4.</param>
/// <param name="Text">Text of the heading as plain text. Required, must not be blank, at most 200 characters.</param>
public sealed record HeadingBlockDto(int Level, string Text) : ContentBlockDto;
