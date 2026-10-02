namespace Knowledge.Domain.Materials.Content;

/// <summary>
/// Catalog of content block types a material's content is built from (ADR-0028).
/// </summary>
/// <remarks>
/// <para>
/// Content is a tree. Some types may appear at the top level (and inside <see cref="Callout"/> / <see cref="Toggle"/>); the "item" types
/// (<see cref="ListItem"/>, <see cref="ChecklistItem"/>, <see cref="TakeawayItem"/>, <see cref="TableRow"/>, <see cref="TableCell"/>,
/// <see cref="TranscriptSegment"/>) exist only as children of their container, and <see cref="Image"/> may additionally be a child of
/// <see cref="Gallery"/>. The complete nesting and field rules are enforced by <see cref="ContentBuilder"/>.
/// </para>
/// <para>
/// Values are stored in the database and exposed in the API by name, so renaming a member is a breaking change. Adding a type requires
/// a new value, validation rules in <see cref="ContentBuilder"/>, possibly new columns and <c>CHECK</c> constraints in a migration, and
/// support in the API contract and clients.
/// </para>
/// </remarks>
public enum BlockType
{
    /// <summary>Section heading of level 1–4 (<see cref="BlockSpec.Level"/>) with unformatted text (<see cref="BlockSpec.PlainText"/>).</summary>
    Heading,

    /// <summary>Paragraph of formatted text (<see cref="BlockSpec.Text"/>).</summary>
    Paragraph,

    /// <summary>Quotation of formatted text with an optional author and source.</summary>
    Quote,

    /// <summary>
    /// Ordered or unordered list (<see cref="ListStyle"/> in <see cref="BlockSpec.Variant"/>); contains at least one <see cref="ListItem"/>.
    /// Lists can be nested up to <see cref="ContentBuilder.MaxListDepth"/> levels.
    /// </summary>
    List,

    /// <summary>List entry with formatted text; may contain nested <see cref="List"/> blocks (only as a child of <see cref="List"/>).</summary>
    ListItem,

    /// <summary>Checklist; contains at least one <see cref="ChecklistItem"/>.</summary>
    Checklist,

    /// <summary>Checklist entry with formatted text and a required ticked state (only as a child of <see cref="Checklist"/>).</summary>
    ChecklistItem,

    /// <summary>
    /// Highlighted box of a <see cref="CalloutVariant"/> with an optional title; contains at least one block of any top-level type except
    /// another <see cref="Callout"/> as a direct child.
    /// </summary>
    Callout,

    /// <summary>
    /// Collapsible section with a required header (<see cref="BlockSpec.Title"/>); contains at least one block of any top-level type.
    /// Toggles can be nested up to <see cref="ContentBuilder.MaxToggleDepth"/> levels.
    /// </summary>
    Toggle,

    /// <summary>"Key takeaways" summary box; contains at least one <see cref="TakeawayItem"/>.</summary>
    KeyTakeaways,

    /// <summary>Single takeaway with formatted text (only as a child of <see cref="KeyTakeaways"/>).</summary>
    TakeawayItem,

    /// <summary>Horizontal separator; has no fields and no children.</summary>
    Divider,

    /// <summary>Image given by an HTTPS URL, with required alternative text and optional caption and credit.</summary>
    Image,

    /// <summary>Gallery of 2 to 12 <see cref="Image"/> blocks.</summary>
    Gallery,

    /// <summary>Video file given by an HTTPS URL, with optional caption and duration. For embedded players use <see cref="Embed"/>.</summary>
    Video,

    /// <summary>Audio recording given by an HTTPS URL, with optional caption and duration.</summary>
    Audio,

    /// <summary>Embedded player of an allowed provider (<see cref="ContentBuilder.AllowedEmbedHosts"/>), with an optional caption.</summary>
    Embed,

    /// <summary>Preview card of an external link (HTTPS) with a required title and an optional description.</summary>
    LinkCard,

    /// <summary>Block of source code (<see cref="BlockSpec.PlainText"/>) with an optional language.</summary>
    Code,

    /// <summary>Table of 1 to 20 <see cref="TableRow"/> blocks, all with the same number of cells; optionally with a header row.</summary>
    Table,

    /// <summary>Table row with 1 to 10 <see cref="TableCell"/> blocks (only as a child of <see cref="Table"/>).</summary>
    TableRow,

    /// <summary>Table cell with formatted text, which may be empty (only as a child of <see cref="TableRow"/>).</summary>
    TableCell,

    /// <summary>Formatted text linked to a moment of the material's main media (<see cref="BlockSpec.AtSeconds"/>), e.g. a chapter mark.</summary>
    Timestamp,

    /// <summary>Transcript of the main media; contains at least one <see cref="TranscriptSegment"/>.</summary>
    Transcript,

    /// <summary>
    /// Transcript segment: formatted text starting at a moment of the recording, with an optional speaker (only as a child of <see cref="Transcript"/>).
    /// </summary>
    TranscriptSegment,
}
