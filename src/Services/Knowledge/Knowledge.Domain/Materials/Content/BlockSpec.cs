namespace Knowledge.Domain.Materials.Content;

/// <summary>
/// Input description of one content block (and, through <see cref="Children"/>, of its whole subtree) passed to
/// <see cref="Material.ReplaceContent"/>. It is the untrusted, not yet validated form of a <see cref="ContentBlock"/>.
/// </summary>
/// <remarks>
/// <para>
/// The Application layer maps the JSON content document of the API (a tree discriminated by <c>type</c>) to a tree of
/// <see cref="BlockSpec"/>s; <see cref="ContentBuilder.Build"/> validates it and turns it into flat <see cref="ContentBlock"/> nodes.
/// One record type serves all block types, so most parameters are optional and only a subset applies to each <see cref="Type"/>.
/// </para>
/// <para>
/// Setting a field that does not apply to the block type is a validation error (<c>knowledge.content.invalid_block</c>), not silently
/// ignored. "Set" means non-null; for <see cref="Children"/> and <see cref="Text"/> an empty list counts as not set. Text fields are not
/// trimmed. The full matrix of allowed and required fields, nesting rules and limits is documented on <see cref="ContentBuilder"/>.
/// </para>
/// </remarks>
/// <example>
/// A callout containing a paragraph with a bold link, and a two-image gallery:
/// <code>
/// BlockSpec[] content =
/// [
///     new(BlockType.Callout, Variant: nameof(CalloutVariant.Tip), Title: "Before you start", Children:
///     [
///         new(BlockType.Paragraph, Text:
///         [
///             new SpanSpec("Read the "),
///             new SpanSpec("sleep hygiene guide", TextMarks.Bold, "https://example.com/guide"),
///         ]),
///     ]),
///     new(BlockType.Gallery, Children:
///     [
///         new(BlockType.Image, Url: "https://cdn.example.com/a.png", AltText: "Bedroom at night"),
///         new(BlockType.Image, Url: "https://cdn.example.com/b.png", AltText: "Alarm clock"),
///     ]),
/// ];
/// </code>
/// </example>
/// <param name="Type">Block type; decides which other parameters are allowed or required and which child types may appear.</param>
/// <param name="Children">
/// Nested blocks in display order: list items of a list, rows of a table, cells of a row, images of a gallery, content of a callout or
/// toggle, and so on. <see langword="null"/> or empty for blocks without children. Allowed child types depend on <see cref="Type"/>.
/// </param>
/// <param name="Text">
/// Formatted text as an ordered list of spans; required (at least one span) for <see cref="BlockType.Paragraph"/>, <see cref="BlockType.Quote"/>,
/// <see cref="BlockType.ListItem"/>, <see cref="BlockType.ChecklistItem"/>, <see cref="BlockType.TakeawayItem"/>, <see cref="BlockType.Timestamp"/>
/// and <see cref="BlockType.TranscriptSegment"/>; optional for <see cref="BlockType.TableCell"/> (an empty cell); not allowed elsewhere.
/// </param>
/// <param name="Level">Heading level from 1 (top) to 4; required for, and only for, <see cref="BlockType.Heading"/>.</param>
/// <param name="Variant">
/// Name of a <see cref="ListStyle"/> value (required for <see cref="BlockType.List"/>) or a <see cref="CalloutVariant"/> value (required for
/// <see cref="BlockType.Callout"/>), e.g. <c>"Ordered"</c> or <c>"Warning"</c>; case-sensitive.
/// </param>
/// <param name="Title">
/// Title of a <see cref="BlockType.Callout"/> (optional), header of a <see cref="BlockType.Toggle"/> (required) or title of a
/// <see cref="BlockType.LinkCard"/> (required); at most 200 characters, not blank when required.
/// </param>
/// <param name="PlainText">
/// Unformatted text of a <see cref="BlockType.Heading"/> (required, at most 200 characters) or source code of a <see cref="BlockType.Code"/>
/// block (required, at most <see cref="ContentBuilder.MaxCodeLength"/> characters); must not be blank.
/// </param>
/// <param name="Url">
/// Absolute HTTPS address (at most <see cref="Knowledge.Domain.Common.WebUrl.MaxLength"/> characters) of an <see cref="BlockType.Image"/>,
/// <see cref="BlockType.Video"/>, <see cref="BlockType.Audio"/>, <see cref="BlockType.Embed"/> (host must be in
/// <see cref="ContentBuilder.AllowedEmbedHosts"/>) or <see cref="BlockType.LinkCard"/>; required for all of them.
/// </param>
/// <param name="AltText">Alternative text of an <see cref="BlockType.Image"/> for screen readers; required, not blank, at most 300 characters.</param>
/// <param name="Caption">
/// Optional caption of an <see cref="BlockType.Image"/>, <see cref="BlockType.Video"/>, <see cref="BlockType.Audio"/> or
/// <see cref="BlockType.Embed"/>; at most 300 characters.
/// </param>
/// <param name="Credit">Optional author or source of an <see cref="BlockType.Image"/>; at most 200 characters.</param>
/// <param name="Language">Optional programming language of a <see cref="BlockType.Code"/> block (e.g. <c>csharp</c>), for syntax highlighting; at most 30 characters.</param>
/// <param name="DurationSeconds">Optional non-negative duration in seconds of a <see cref="BlockType.Video"/> or <see cref="BlockType.Audio"/> block.</param>
/// <param name="AtSeconds">
/// Position in the material's main media, in seconds from the start (non-negative); required for <see cref="BlockType.Timestamp"/> and
/// <see cref="BlockType.TranscriptSegment"/>.
/// </param>
/// <param name="Speaker">Optional speaker of a <see cref="BlockType.TranscriptSegment"/>; at most 100 characters.</param>
/// <param name="IsChecked">Whether a <see cref="BlockType.ChecklistItem"/> is ticked; required for it (<see langword="false"/> is a valid value).</param>
/// <param name="HasHeaderRow">Whether the first row of a <see cref="BlockType.Table"/> is a header row; optional (<see langword="null"/> means not specified).</param>
/// <param name="Author">Optional author of a <see cref="BlockType.Quote"/>; at most 200 characters.</param>
/// <param name="Source">Optional source (book, article, speech) of a <see cref="BlockType.Quote"/>; at most 200 characters.</param>
/// <param name="Description">Optional description of a <see cref="BlockType.LinkCard"/>; at most 500 characters.</param>
public sealed record BlockSpec(
    BlockType Type,
    IReadOnlyList<BlockSpec>? Children = null,
    IReadOnlyList<SpanSpec>? Text = null,
    int? Level = null,
    string? Variant = null,
    string? Title = null,
    string? PlainText = null,
    string? Url = null,
    string? AltText = null,
    string? Caption = null,
    string? Credit = null,
    string? Language = null,
    int? DurationSeconds = null,
    int? AtSeconds = null,
    string? Speaker = null,
    bool? IsChecked = null,
    bool? HasHeaderRow = null,
    string? Author = null,
    string? Source = null,
    string? Description = null);
