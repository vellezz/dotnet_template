namespace Knowledge.Api.Controllers;

/// <summary>
/// Request body of <c>PUT /v1/materials/{materialId}</c>: the new title, description and main media of a material. All fields are
/// replaced; omitted optional fields are treated as <see langword="null"/> and remove the current value.
/// </summary>
/// <param name="Title">The new title: required, at most 200 characters (trimmed).</param>
/// <param name="Description">The new description, at most 2000 characters; <see langword="null"/> or a blank string removes it.</param>
/// <param name="MainMediaUrl">
/// Absolute <c>https</c> URL of the main media (the video or audio file); only for materials of type <c>Video</c> and <c>Podcast</c>,
/// must be <see langword="null"/> for an article. <see langword="null"/> removes the main media, which a published video or podcast
/// cannot do.
/// </param>
/// <param name="MainMediaDurationSeconds">
/// Duration of the main media in seconds (zero or more); allowed only together with <paramref name="MainMediaUrl"/>,
/// otherwise <see langword="null"/>.
/// </param>
public sealed record UpdateMaterialDetailsRequest(string Title, string? Description, string? MainMediaUrl, int? MainMediaDurationSeconds);
