namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Audio block (discriminator <c>"type": "audio"</c>): an audio player embedded in the material content, pointing at an audio file.
/// </summary>
/// <remarks>
/// <para>JSON shape: <c>{ "type": "audio", "url": "https://...", "caption": "...", "durationSeconds": 1260 }</c>.</para>
/// <para>
/// Allowed at the top level and inside <see cref="CalloutBlockDto"/> and <see cref="ToggleBlockDto"/>. The block is independent of
/// the material's main media (<c>MainMediaUrl</c>); use it for additional recordings inside the text.
/// Rule violations are reported as <c>knowledge.content.invalid_block</c> (HTTP 400).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// { "type": "audio", "url": "https://cdn.example.com/breathing.mp3", "caption": "4-7-8 breathing", "durationSeconds": 300 }
/// </code>
/// </example>
/// <param name="Url">
/// Address of the audio file. Required; an absolute <c>https</c> URL of at most
/// <see cref="Knowledge.Domain.Common.WebUrl.MaxLength"/> characters.
/// </param>
/// <param name="Caption">Optional caption shown below the player, at most 300 characters; <see langword="null"/> when there is none.</param>
/// <param name="DurationSeconds">Optional length of the recording in whole seconds, not negative; <see langword="null"/> when unknown.</param>
public sealed record AudioBlockDto(string Url, string? Caption = null, int? DurationSeconds = null) : ContentBlockDto;
