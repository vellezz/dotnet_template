namespace Knowledge.Infrastructure.Persistence.Read.Models;

/// <summary>Read model of one row of <c>knowledge.Collections</c> (the <c>Collection</c> aggregate without its items and categories).</summary>
internal sealed class CollectionRow
{
    /// <summary>Identifier of the collection (primary key).</summary>
    public Guid Id { get; init; }

    /// <summary>Title of the collection.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Publication status stored as the enum name: <c>Draft</c>, <c>Published</c> or <c>Archived</c>. Compare with <c>nameof(PublicationStatus.Published)</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Moment of the first publication; <see langword="null"/> for a collection that was never published.</summary>
    public DateTimeOffset? PublishedAt { get; init; }
}
