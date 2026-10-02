namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Video block (discriminator <c>"type": "video"</c>): a video player embedded in the material content, pointing at a video file.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "video", "url": "https://...", "caption": "...", "durationSeconds": 95 }</c>.</para>
/// <para>
/// Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>. For videos hosted by YouTube or
/// Vimeo use <see cref="EmbedBlockDto"/>; this block expects a direct link to a file. It is independent of the material's main media
/// (<c>MainMediaUrl</c>). Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "video", "url": "https://cdn.example.com/stretching.mp4", "durationSeconds": 95 }
/// </code>
/// </example>
/// <param name="Url">
/// Address of the video file. Required; an absolute <c>https</c> URL of at most
/// <see cref="Knowledge.Domain.Common.WebUrl.MaxLength"/> characters.
/// </param>
/// <param name="Caption">Optional caption shown below the player, at most 300 characters; <see langword="null"/> when there is none.</param>
/// <param name="DurationSeconds">Optional length of the video in whole seconds, not negative; <see langword="null"/> when unknown.</param>
public sealed record VideoBlockDto(string Url, string? Caption = null, int? DurationSeconds = null) : ContentBlockDto;
