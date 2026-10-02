namespace Knowledge.Infrastructure.Persistence.Read.Models;

/// <summary>
/// Read model of one row of <c>knowledge.ContentBlocks</c>: one node of the block content tree of a material (ADR-0028).
/// </summary>
/// <remarks>
/// All block types share one table; the columns after <see cref="Type"/> are used only by some types and are <see langword="null"/>
/// otherwise (the database enforces the required ones with <c>CHECK</c> constraints). Composite elements such as list items, table rows and
/// cells, gallery images and transcript segments are child rows of their container block. Assembled into a tree by
/// <c>ContentTreeReader</c>.
/// </remarks>
internal sealed class ContentBlockRow
{
    /// <summary>Identifier of the block (primary key).</summary>
    public Guid Id { get; init; }

    /// <summary>Identifier of the material that owns the block.</summary>
    public Guid MaterialId { get; init; }

    /// <summary>Identifier of the parent block; <see langword="null"/> for a top-level block.</summary>
    public Guid? ParentBlockId { get; init; }

    /// <summary>Zero-based position among the siblings with the same parent.</summary>
    public int Position { get; init; }

    /// <summary>Block type stored as the <c>BlockType</c> enum name, e.g. <c>Heading</c>, <c>ListItem</c>, <c>TableCell</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Heading level 1–4 (<c>Heading</c> only).</summary>
    public int? Level { get; init; }

    /// <summary>List style (<c>Ordered</c>, <c>Unordered</c>) of a <c>List</c> or variant (<c>Info</c>, <c>Tip</c>, <c>Warning</c>, <c>Important</c>) of a <c>Callout</c>.</summary>
    public string? Variant { get; init; }

    /// <summary>Title of a <c>Callout</c>, <c>Toggle</c> or <c>LinkCard</c>.</summary>
    public string? Title { get; init; }

    /// <summary>Unformatted text: the text of a <c>Heading</c> or the source code of a <c>Code</c> block.</summary>
    public string? PlainText { get; init; }

    /// <summary>Absolute <c>https</c> URL of an <c>Image</c>, <c>Video</c>, <c>Audio</c>, <c>Embed</c> or <c>LinkCard</c>.</summary>
    public string? Url { get; init; }

    /// <summary>Alternative text of an <c>Image</c>.</summary>
    public string? AltText { get; init; }

    /// <summary>Caption of an <c>Image</c>, <c>Video</c>, <c>Audio</c> or <c>Embed</c>.</summary>
    public string? Caption { get; init; }

    /// <summary>Author or source credit of an <c>Image</c>.</summary>
    public string? Credit { get; init; }

    /// <summary>Language of the source code in a <c>Code</c> block, e.g. <c>bash</c>.</summary>
    public string? Language { get; init; }

    /// <summary>Duration in seconds of a <c>Video</c> or <c>Audio</c> block.</summary>
    public int? DurationSeconds { get; init; }

    /// <summary>Time offset in seconds of a <c>Timestamp</c> or <c>TranscriptSegment</c>.</summary>
    public int? AtSeconds { get; init; }

    /// <summary>Speaker of a <c>TranscriptSegment</c>.</summary>
    public string? Speaker { get; init; }

    /// <summary>Whether a <c>ChecklistItem</c> is checked.</summary>
    public bool? IsChecked { get; init; }

    /// <summary>Whether the first row of a <c>Table</c> is a header row.</summary>
    public bool? HasHeaderRow { get; init; }

    /// <summary>Author of a <c>Quote</c>.</summary>
    public string? Author { get; init; }

    /// <summary>Source of a <c>Quote</c>.</summary>
    public string? Source { get; init; }

    /// <summary>Description of a <c>LinkCard</c>.</summary>
    public string? Description { get; init; }
}
