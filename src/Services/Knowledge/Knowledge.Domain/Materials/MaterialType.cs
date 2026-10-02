namespace Knowledge.Domain.Materials;

/// <summary>
/// Kind of a <see cref="Material"/>; chosen in <see cref="Material.Create"/> and immutable afterwards.
/// </summary>
/// <remarks>
/// The kind decides the rules for main media (<see cref="Material.MainMediaUrl"/>): an article must not have it, a video or podcast needs it
/// to be published. All kinds use the same block content. The value is stored and published (<c>MaterialPublishedV1</c>) by its name,
/// so renaming a member is a breaking change.
/// </remarks>
public enum MaterialType
{
    /// <summary>Written article: block content only; setting main media fails with <see cref="MaterialErrors.MediaNotAllowedForArticle"/>.</summary>
    Article,

    /// <summary>Video: the main media is the video file or stream; publishing requires it (<see cref="MaterialErrors.MainMediaRequired"/>).</summary>
    Video,

    /// <summary>Podcast episode: the main media is the audio file; publishing requires it (<see cref="MaterialErrors.MainMediaRequired"/>).</summary>
    Podcast,
}
