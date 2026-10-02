namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Code block (discriminator <c>"type": "code"</c>): a snippet of source code shown in a monospaced font.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "code", "code": "...", "language": "csharp" }</c>.</para>
/// <para>
/// The code is plain text: it has no formatting spans and is stored as sent, line breaks included. Its words count towards the
/// material's reading time. Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "code", "code": "var total = items.Sum();", "language": "csharp" }
/// </code>
/// </example>
/// <param name="Code">
/// The source code as plain text. Required, must not be blank, at most
/// <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxCodeLength"/> characters.
/// </param>
/// <param name="Language">
/// Optional language identifier for syntax highlighting, for example <c>csharp</c> or <c>json</c>; free text that is not checked against
/// a list, at most 30 characters. <see langword="null"/> when not specified.
/// </param>
public sealed record CodeBlockDto(string Code, string? Language = null) : ContentBlockDto;
