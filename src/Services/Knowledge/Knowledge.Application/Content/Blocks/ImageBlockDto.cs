namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Image block (discriminator <c>"type": "image"</c>): a single illustration. The same record describes each image of a
/// <see cref="GalleryBlockDto"/>.
/// </summary>
/// <remarks>
/// <para>
/// JSON shape: <c>{ "type": "image", "url": "https://...", "altText": "...", "caption": "...", "credit": "..." }</c>
/// (inside a gallery the <c>type</c> property is omitted).
/// </para>
/// <para>
/// The service stores only the link; images are hosted elsewhere. Allowed at the top level, inside <see cref="CalloutBlockDto"/> and
/// <see cref="ToggleBlockDto"/>, and as an image of a gallery. Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "image", "url": "https://cdn.example.com/bedroom.jpg", "altText": "A dark bedroom with blackout curtains", "credit": "Photo: J. Doe" }
/// </code>
/// </example>
/// <param name="Url">
/// Address of the image. Required; an absolute <c>https</c> URL of at most <see cref="Knowledge.Domain.Common.WebUrl.MaxLength"/> characters.
/// </param>
/// <param name="AltText">
/// Alternative text describing the image for screen readers and for when the image cannot be loaded. Required, must not be blank,
/// at most 300 characters.
/// </param>
/// <param name="Caption">Optional caption shown below the image, at most 300 characters; <see langword="null"/> when there is none.</param>
/// <param name="Credit">Optional author or source of the image, at most 200 characters; <see langword="null"/> when there is none.</param>
public sealed record ImageBlockDto(string Url, string AltText, string? Caption = null, string? Credit = null) : ContentBlockDto;
