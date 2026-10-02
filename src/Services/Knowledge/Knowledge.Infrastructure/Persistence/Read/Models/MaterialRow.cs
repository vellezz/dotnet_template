namespace Knowledge.Infrastructure.Persistence.Read.Models;

/// <summary>
/// Read model of one row of <c>knowledge.Materials</c> (the <c>Material</c> aggregate without its categories and content).
/// </summary>
internal sealed class MaterialRow
{
    /// <summary>Identifier of the material (primary key).</summary>
    public Guid Id { get; init; }

    /// <summary>Material type stored as the enum name: <c>Article</c>, <c>Video</c> or <c>Podcast</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Title of the material.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary><c>https</c> URL of the main video or audio file; <see langword="null"/> for articles and for media not set yet.</summary>
    public string? MainMediaUrl { get; init; }

    /// <summary>Duration of the main media in seconds, when known.</summary>
    public int? MainMediaDurationSeconds { get; init; }

    /// <summary>Publication status stored as the enum name: <c>Draft</c>, <c>Published</c> or <c>Archived</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Estimated reading time computed from the content; 0 when the content is empty.</summary>
    public int ReadingTimeMinutes { get; init; }

    /// <summary>Moment of the last change of the material.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Moment of publication; <see langword="null"/> for a material that was never published.</summary>
    public DateTimeOffset? PublishedAt { get; init; }
}
