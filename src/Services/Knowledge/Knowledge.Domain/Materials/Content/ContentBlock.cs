namespace Knowledge.Domain.Materials.Content;

/// <summary>
/// One node of a material's content tree, owned by the <see cref="Material"/> aggregate and stored as one row of the
/// <c>ContentBlocks</c> table (ADR-0028).
/// </summary>
/// <remarks>
/// <para>
/// All block types share this single class and table: <see cref="Type"/> decides which of the nullable properties are filled and the others
/// stay <see langword="null"/> (see the field matrix on <see cref="ContentBuilder"/>). The tree is stored flat: every node points to its parent
/// through <see cref="ParentBlockId"/> and is ordered among its siblings by <see cref="Position"/>; formatted text lives in child
/// <see cref="Spans"/> (table <c>ContentTextSpans</c>).
/// </para>
/// <para>
/// Instances are immutable after creation and can only be created by <see cref="ContentBuilder"/>, which validates the whole tree first;
/// database <c>CHECK</c> constraints repeat the most important rules as a safety net. A block is never edited in place:
/// <see cref="Material.ReplaceContent"/> replaces all blocks with new ones (and new IDs). Query handlers rebuild the tree from the rows
/// for the API; they read the read model, not this class.
/// </para>
/// </remarks>
/// <seealso cref="BlockSpec"/>
/// <seealso cref="ContentBuilder"/>
public sealed class ContentBlock
{
    private readonly List<TextSpan> _spans = [];

    private ContentBlock(BlockId id) => Id = id;

    /// <summary>Gets the block identifier; newly generated on every content replacement, so it is not stable across edits.</summary>
    public BlockId Id { get; private set; }

    /// <summary>Gets the identifier of the parent block, or <see langword="null"/> for a top-level block.</summary>
    public BlockId? ParentBlockId { get; private set; }

    /// <summary>Gets the zero-based position among the siblings (blocks with the same parent), in display order.</summary>
    public int Position { get; private set; }

    /// <summary>Gets the block type, which determines which of the other properties are filled.</summary>
    public BlockType Type { get; private set; }

    /// <summary>Gets the heading level from 1 to 4; set only for <see cref="BlockType.Heading"/>.</summary>
    public int? Level { get; private set; }

    /// <summary>
    /// Gets the list style (<see cref="ListStyle"/>) of a <see cref="BlockType.List"/> or the variant (<see cref="CalloutVariant"/>) of a
    /// <see cref="BlockType.Callout"/>, as the enum member name.
    /// </summary>
    public string? Variant { get; private set; }

    /// <summary>Gets the title of a callout (optional), the header of a <see cref="BlockType.Toggle"/> or the title of a link card.</summary>
    public string? Title { get; private set; }

    /// <summary>Gets the unformatted text of a heading or the source code of a code block.</summary>
    public string? PlainText { get; private set; }

    /// <summary>Gets the HTTPS address of an image, video, audio, embed or link card.</summary>
    public string? Url { get; private set; }

    /// <summary>Gets the alternative text of an image.</summary>
    public string? AltText { get; private set; }

    /// <summary>Gets the caption of an image, video, audio or embed.</summary>
    public string? Caption { get; private set; }

    /// <summary>Gets the author or source credit of an image.</summary>
    public string? Credit { get; private set; }

    /// <summary>Gets the programming language of a code block.</summary>
    public string? Language { get; private set; }

    /// <summary>Gets the non-negative duration of a video or audio block in seconds.</summary>
    public int? DurationSeconds { get; private set; }

    /// <summary>Gets the moment in the main media, in seconds from the start, of a timestamp or transcript segment.</summary>
    public int? AtSeconds { get; private set; }

    /// <summary>Gets the speaker of a transcript segment.</summary>
    public string? Speaker { get; private set; }

    /// <summary>Gets whether a checklist item is ticked; always set for <see cref="BlockType.ChecklistItem"/>.</summary>
    public bool? IsChecked { get; private set; }

    /// <summary>Gets whether the first row of a table is a header row; <see langword="null"/> when not specified.</summary>
    public bool? HasHeaderRow { get; private set; }

    /// <summary>Gets the author of a quote.</summary>
    public string? Author { get; private set; }

    /// <summary>Gets the source of a quote.</summary>
    public string? Source { get; private set; }

    /// <summary>Gets the description of a link card.</summary>
    public string? Description { get; private set; }

    /// <summary>Gets the spans of formatted text in <see cref="TextSpan.Position"/> order; empty for blocks without formatted text.</summary>
    public IReadOnlyList<TextSpan> Spans => _spans;

    /// <summary>
    /// Creates a node from an already validated specification, with a new <see cref="BlockId"/>. Only <see cref="ContentBuilder"/> may call it;
    /// the method itself validates nothing.
    /// </summary>
    /// <param name="parentId">Identifier of the parent node, or <see langword="null"/> for a top-level block.</param>
    /// <param name="position">Zero-based index of the block among its siblings.</param>
    /// <param name="spec">The validated specification; its children are not processed here but by the builder.</param>
    /// <returns>The new node with its text spans.</returns>
    internal static ContentBlock Create(BlockId? parentId, int position, BlockSpec spec)
    {
        var block = new ContentBlock(BlockId.New())
        {
            ParentBlockId = parentId,
            Position = position,
            Type = spec.Type,
            Level = spec.Level,
            Variant = spec.Variant,
            Title = spec.Title,
            PlainText = spec.PlainText,
            Url = spec.Url,
            AltText = spec.AltText,
            Caption = spec.Caption,
            Credit = spec.Credit,
            Language = spec.Language,
            DurationSeconds = spec.DurationSeconds,
            AtSeconds = spec.AtSeconds,
            Speaker = spec.Speaker,
            IsChecked = spec.IsChecked,
            HasHeaderRow = spec.HasHeaderRow,
            Author = spec.Author,
            Source = spec.Source,
            Description = spec.Description,
        };

        block._spans.AddRange((spec.Text ?? []).Select((span, position) => TextSpan.Create(position, span)));
        return block;
    }
}
