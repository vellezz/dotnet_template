namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Embed block (discriminator <c>"type": "embed"</c>): a player of an external provider (YouTube, Vimeo, Spotify) embedded in the content.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "embed", "url": "https://www.youtube.com/watch?v=...", "caption": "..." }</c>.</para>
/// <para>
/// Only hosts listed in <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.AllowedEmbedHosts"/> are accepted
/// (<c>www.youtube.com</c>, <c>youtube.com</c>, <c>youtu.be</c>, <c>player.vimeo.com</c>, <c>vimeo.com</c>, <c>open.spotify.com</c>,
/// compared case-insensitively). The service stores the link only; turning it into a player is up to the client.
/// Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "embed", "url": "https://open.spotify.com/episode/abc123", "caption": "Episode 12" }
/// </code>
/// </example>
/// <param name="Url">
/// Address of the embedded resource. Required; an absolute <c>https</c> URL of at most
/// <see cref="Knowledge.Domain.Common.WebUrl.MaxLength"/> characters whose host is one of the allowed providers.
/// </param>
/// <param name="Caption">Optional caption shown below the embed, at most 300 characters; <see langword="null"/> when there is none.</param>
public sealed record EmbedBlockDto(string Url, string? Caption = null) : ContentBlockDto;
