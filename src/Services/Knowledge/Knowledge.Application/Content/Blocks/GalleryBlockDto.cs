namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Gallery block (discriminator <c>"type": "gallery"</c>): a set of images shown together, for example as a grid or a carousel.
/// </summary>
/// <remarks>
/// <para>
/// JSON shape: <c>{ "type": "gallery", "images": [ { "url": "https://...", "altText": "...", "caption": "...", "credit": "..." } ] }</c>.
/// The images have the fields of <see cref="ImageBlockDto"/>; inside a gallery they are written without a <c>type</c> property.
/// </para>
/// <para>
/// Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "gallery", "images": [
///     { "url": "https://cdn.example.com/1.png", "altText": "Bedroom before" },
///     { "url": "https://cdn.example.com/2.png", "altText": "Bedroom after" } ] }
/// </code>
/// </example>
/// <param name="Images">Images of the gallery in display order; from 2 to 12 items, each following the rules of <see cref="ImageBlockDto"/>.</param>
public sealed record GalleryBlockDto(IReadOnlyList<ImageBlockDto> Images) : ContentBlockDto;
