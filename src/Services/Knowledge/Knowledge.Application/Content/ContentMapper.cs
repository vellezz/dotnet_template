using Knowledge.Application.Content.Blocks;
using Knowledge.Domain.Materials.Content;

namespace Knowledge.Application.Content;

/// <summary>
/// Converts material content between the HTTP contract (<see cref="ContentBlockDto"/> tree) and the domain's
/// <see cref="BlockSpec"/> tree, in both directions.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BlockSpec"/> is the single, type-agnostic description of a block used by the domain: one record with optional fields
/// for every block type plus <c>Children</c>. Composite items that are separate records in the contract (list items, checklist items,
/// takeaways, table rows and cells, gallery images, transcript segments) become child specs of their block. The same tree is used on
/// both sides of CQRS:
/// </para>
/// <list type="bullet">
///   <item><description>Write side: <c>ReplaceMaterialContentHandler</c> calls <see cref="ToSpecs"/> and passes the result to
///   <c>Material.ReplaceContent</c>, which validates it and turns it into content block entities.</description></item>
///   <item><description>Read side: the <c>GetMaterial</c> query handler in Infrastructure rebuilds the spec tree from the
///   <c>ContentBlocks</c> and <c>ContentTextSpans</c> rows and calls <see cref="ToDtos"/>.</description></item>
/// </list>
/// <para>
/// The mapper performs no validation: it copies values as they are and replaces missing lists with empty ones. All content rules live
/// in the aggregate, so invalid input is reported there as <c>knowledge.content.invalid_block</c>. Formatting marks are combined into a
/// <see cref="TextMarks"/> flag value on the way in and split back into a list of single flags on the way out.
/// </para>
/// <para>When adding a block type, extend both switch expressions and the <c>[JsonDerivedType]</c> list on <see cref="ContentBlockDto"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// var result = material.ReplaceContent(ContentMapper.ToSpecs(command.Blocks), clock.UtcNow);
/// </code>
/// </example>
public static class ContentMapper
{
    /// <summary>
    /// Converts content blocks received in a request into the <see cref="BlockSpec"/> tree expected by the <c>Material</c> aggregate.
    /// Content rules are not checked here; the aggregate checks them.
    /// </summary>
    /// <param name="blocks">Top-level blocks from the request in display order; <see langword="null"/> is treated as an empty list.</param>
    /// <returns>Top-level block specs in the same order, each with its nested children.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A block is of a <see cref="ContentBlockDto"/> subtype the mapper does not know.</exception>
    public static IReadOnlyList<BlockSpec> ToSpecs(IReadOnlyList<ContentBlockDto>? blocks) =>
        (blocks ?? []).Select(ToSpec).ToList();

    /// <summary>
    /// Converts a <see cref="BlockSpec"/> tree, rebuilt from stored rows on the read side, into content blocks of the HTTP contract.
    /// </summary>
    /// <remarks>
    /// Assumes the tree was stored by the aggregate and is therefore valid; missing optional values fall back to defaults
    /// (for example heading level 1, empty text, <c>atSeconds</c> 0).
    /// </remarks>
    /// <param name="specs">Top-level block specs in display order.</param>
    /// <returns>Top-level content blocks in the same order, with nested blocks and items.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A spec in <paramref name="specs"/> has a type that can only appear as an item of another block (for example <c>ListItem</c>).
    /// </exception>
    public static IReadOnlyList<ContentBlockDto> ToDtos(IReadOnlyList<BlockSpec> specs) =>
        specs.Select(ToDto).ToList();

    private static BlockSpec ToSpec(ContentBlockDto block) => block switch
    {
        HeadingBlockDto heading => new BlockSpec(BlockType.Heading, Level: heading.Level, PlainText: heading.Text),
        ParagraphBlockDto paragraph => new BlockSpec(BlockType.Paragraph, Text: Spans(paragraph.Text)),
        QuoteBlockDto quote => new BlockSpec(BlockType.Quote, Text: Spans(quote.Text), Author: quote.Author, Source: quote.Source),
        ListBlockDto list => ListSpec(list),
        ChecklistBlockDto checklist => new BlockSpec(
            BlockType.Checklist,
            Children: (checklist.Items ?? []).Select(item => new BlockSpec(BlockType.ChecklistItem, Text: Spans(item.Text), IsChecked: item.IsChecked)).ToList()),
        CalloutBlockDto callout => new BlockSpec(BlockType.Callout, Children: ToSpecs(callout.Blocks), Variant: callout.Variant.ToString(), Title: callout.Title),
        ToggleBlockDto toggle => new BlockSpec(BlockType.Toggle, Children: ToSpecs(toggle.Blocks), Title: toggle.Title),
        KeyTakeawaysBlockDto takeaways => new BlockSpec(
            BlockType.KeyTakeaways,
            Children: (takeaways.Items ?? []).Select(item => new BlockSpec(BlockType.TakeawayItem, Text: Spans(item.Text))).ToList()),
        DividerBlockDto => new BlockSpec(BlockType.Divider),
        ImageBlockDto image => ImageSpec(image),
        GalleryBlockDto gallery => new BlockSpec(BlockType.Gallery, Children: (gallery.Images ?? []).Select(ImageSpec).ToList()),
        VideoBlockDto video => new BlockSpec(BlockType.Video, Url: video.Url, Caption: video.Caption, DurationSeconds: video.DurationSeconds),
        AudioBlockDto audio => new BlockSpec(BlockType.Audio, Url: audio.Url, Caption: audio.Caption, DurationSeconds: audio.DurationSeconds),
        EmbedBlockDto embed => new BlockSpec(BlockType.Embed, Url: embed.Url, Caption: embed.Caption),
        LinkCardBlockDto card => new BlockSpec(BlockType.LinkCard, Url: card.Url, Title: card.Title, Description: card.Description),
        CodeBlockDto code => new BlockSpec(BlockType.Code, PlainText: code.Code, Language: code.Language),
        TableBlockDto table => new BlockSpec(
            BlockType.Table,
            HasHeaderRow: table.HasHeaderRow,
            Children: (table.Rows ?? []).Select(row => new BlockSpec(
                BlockType.TableRow,
                Children: (row.Cells ?? []).Select(cell => new BlockSpec(BlockType.TableCell, Text: Spans(cell.Text))).ToList())).ToList()),
        TimestampBlockDto timestamp => new BlockSpec(BlockType.Timestamp, Text: Spans(timestamp.Text), AtSeconds: timestamp.AtSeconds),
        TranscriptBlockDto transcript => new BlockSpec(
            BlockType.Transcript,
            Children: (transcript.Segments ?? []).Select(segment => new BlockSpec(
                BlockType.TranscriptSegment, Text: Spans(segment.Text), AtSeconds: segment.AtSeconds, Speaker: segment.Speaker)).ToList()),
        _ => throw new ArgumentOutOfRangeException(nameof(block), block.GetType().Name, "Nieobsługiwany typ bloku."),
    };

    private static BlockSpec ListSpec(ListBlockDto list) => new(
        BlockType.List,
        Variant: list.Style.ToString(),
        Children: (list.Items ?? []).Select(item => new BlockSpec(
            BlockType.ListItem,
            Text: Spans(item.Text),
            Children: item.Children is null ? null : [ListSpec(item.Children)])).ToList());

    private static BlockSpec ImageSpec(ImageBlockDto image) =>
        new(BlockType.Image, Url: image.Url, AltText: image.AltText, Caption: image.Caption, Credit: image.Credit);

    private static List<SpanSpec> Spans(IReadOnlyList<SpanDto>? spans) =>
        (spans ?? []).Select(span => new SpanSpec(
            span.Text,
            (span.Marks ?? []).Aggregate(TextMarks.None, (marks, mark) => marks | mark),
            span.Link)).ToList();

    private static ContentBlockDto ToDto(BlockSpec spec) => spec.Type switch
    {
        BlockType.Heading => new HeadingBlockDto(spec.Level ?? 1, spec.PlainText ?? string.Empty),
        BlockType.Paragraph => new ParagraphBlockDto(SpanDtos(spec)),
        BlockType.Quote => new QuoteBlockDto(SpanDtos(spec), spec.Author, spec.Source),
        BlockType.List => ListDto(spec),
        BlockType.Checklist => new ChecklistBlockDto(Children(spec).Select(item => new ChecklistItemDto(SpanDtos(item), item.IsChecked ?? false)).ToList()),
        BlockType.Callout => new CalloutBlockDto(Enum.Parse<CalloutVariant>(spec.Variant!), ToDtos(Children(spec)), spec.Title),
        BlockType.Toggle => new ToggleBlockDto(spec.Title ?? string.Empty, ToDtos(Children(spec))),
        BlockType.KeyTakeaways => new KeyTakeawaysBlockDto(Children(spec).Select(item => new TakeawayItemDto(SpanDtos(item))).ToList()),
        BlockType.Divider => new DividerBlockDto(),
        BlockType.Image => ImageDto(spec),
        BlockType.Gallery => new GalleryBlockDto(Children(spec).Select(ImageDto).ToList()),
        BlockType.Video => new VideoBlockDto(spec.Url!, spec.Caption, spec.DurationSeconds),
        BlockType.Audio => new AudioBlockDto(spec.Url!, spec.Caption, spec.DurationSeconds),
        BlockType.Embed => new EmbedBlockDto(spec.Url!, spec.Caption),
        BlockType.LinkCard => new LinkCardBlockDto(spec.Url!, spec.Title ?? string.Empty, spec.Description),
        BlockType.Code => new CodeBlockDto(spec.PlainText ?? string.Empty, spec.Language),
        BlockType.Table => new TableBlockDto(
            spec.HasHeaderRow ?? false,
            Children(spec).Select(row => new TableRowDto(Children(row).Select(cell => new TableCellDto(SpanDtos(cell))).ToList())).ToList()),
        BlockType.Timestamp => new TimestampBlockDto(spec.AtSeconds ?? 0, SpanDtos(spec)),
        BlockType.Transcript => new TranscriptBlockDto(
            Children(spec).Select(segment => new TranscriptSegmentDto(segment.AtSeconds ?? 0, SpanDtos(segment), segment.Speaker)).ToList()),
        _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Type, "Blok nie może wystąpić na tym poziomie."),
    };

    private static ListBlockDto ListDto(BlockSpec list) => new(
        Enum.Parse<ListStyle>(list.Variant!),
        Children(list).Select(item => new ListItemDto(
            SpanDtos(item),
            Children(item).FirstOrDefault(child => child.Type == BlockType.List) is { } nested ? ListDto(nested) : null)).ToList());

    private static ImageBlockDto ImageDto(BlockSpec image) => new(image.Url!, image.AltText ?? string.Empty, image.Caption, image.Credit);

    private static IReadOnlyList<BlockSpec> Children(BlockSpec spec) => spec.Children ?? [];

    private static List<SpanDto> SpanDtos(BlockSpec spec) =>
        (spec.Text ?? []).Select(span => new SpanDto(
            span.Text,
            span.Marks == TextMarks.None ? null : Enum.GetValues<TextMarks>().Where(mark => mark != TextMarks.None && span.Marks.HasFlag(mark)).ToList(),
            span.LinkHref)).ToList();
}
