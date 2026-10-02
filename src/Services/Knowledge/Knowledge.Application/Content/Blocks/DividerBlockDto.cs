namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Divider block (discriminator <c>"type": "divider"</c>): a horizontal line that separates sections of content. It has no fields.
/// </summary>
/// <remarks>
/// JSON shape: <c>{ "type": "divider" }</c>. Allowed at the top level and inside <see cref="CalloutBlockDto"/> and
/// <see cref="ToggleBlockDto"/>.
/// </remarks>
public sealed record DividerBlockDto : ContentBlockDto;
