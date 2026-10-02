namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Link card block (discriminator <c>"type": "linkCard"</c>): a prominent link to an external page, shown with a title and a description.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "linkCard", "url": "https://...", "title": "...", "description": "..." }</c>.</para>
/// <para>
/// The service does not fetch the target page; title and description are written by the editor. For a link inside running text use
/// <see cref="SpanDto.Link"/> instead. Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "linkCard", "url": "https://www.example.org/sleep-guide", "title": "Sleep guide", "description": "A practical checklist." }
/// </code>
/// </example>
/// <param name="Url">
/// Target address. Required; an absolute <c>https</c> URL of at most <see cref="Knowledge.Domain.Common.WebUrl.MaxLength"/> characters.
/// </param>
/// <param name="Title">Title of the card. Required, must not be blank, at most 200 characters.</param>
/// <param name="Description">Optional description of the target page, at most 500 characters; <see langword="null"/> when there is none.</param>
public sealed record LinkCardBlockDto(string Url, string Title, string? Description = null) : ContentBlockDto;
