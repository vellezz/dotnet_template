using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Common;

namespace Knowledge.Domain.Materials.Content;

/// <summary>
/// Validates a tree of <see cref="BlockSpec"/>s against all content rules of ADR-0028 and flattens it into <see cref="ContentBlock"/> nodes,
/// computing the plain text and reading time on the way. The single place where the rules of block content live.
/// </summary>
/// <remarks>
/// <para><b>How it works.</b> <see cref="Build"/> walks the tree depth-first (a block, then its children, then the next sibling). For each
/// block it checks, in this order: the block type is allowed at this place, the list and toggle nesting depth, the total block count,
/// the fields (no field foreign to the type, then required fields and formats), the text spans, and the number of children. The first
/// violation stops the walk and is returned as a validation error <c>knowledge.content.invalid_block</c> (HTTP 400) whose message is
/// <c>"{path}: {reason}."</c>. The path locates the block in the input: <c>blocks[2]</c> is the third top-level block,
/// <c>blocks[2].children[0].children[1]</c> a grandchild. Nothing is returned partially: on error no nodes are produced.</para>
/// <para><b>Nesting rules</b> (allowed children; any type not listed has no children):</para>
/// <list type="table">
///   <listheader><term>Parent</term><description>Allowed child types and count</description></listheader>
///   <item><term>top level</term><description>Heading, Paragraph, Quote, List, Checklist, Callout, Toggle, KeyTakeaways, Divider, Image,
///   Gallery, Video, Audio, Embed, LinkCard, Code, Table, Timestamp, Transcript; at most <see cref="MaxBlocks"/> blocks in the whole tree.</description></item>
///   <item><term>List</term><description>ListItem, at least 1. Nesting (List → ListItem → List) up to <see cref="MaxListDepth"/> list levels,
///   counted from the root even through callouts and toggles.</description></item>
///   <item><term>ListItem</term><description>List (optional, for nested lists).</description></item>
///   <item><term>Checklist</term><description>ChecklistItem, at least 1.</description></item>
///   <item><term>KeyTakeaways</term><description>TakeawayItem, at least 1.</description></item>
///   <item><term>Gallery</term><description>Image, from 2 to 12.</description></item>
///   <item><term>Table</term><description>TableRow, from 1 to 20; all rows must have the same number of cells.</description></item>
///   <item><term>TableRow</term><description>TableCell, from 1 to 10.</description></item>
///   <item><term>Transcript</term><description>TranscriptSegment, at least 1.</description></item>
///   <item><term>Callout</term><description>At least 1 block of any top-level type except Callout.</description></item>
///   <item><term>Toggle</term><description>At least 1 block of any top-level type, including Toggle up to <see cref="MaxToggleDepth"/> levels.</description></item>
/// </list>
/// <para><b>Fields per type</b> (any other non-null field is an error; lengths in characters):</para>
/// <list type="table">
///   <listheader><term>Type</term><description>Fields (required unless marked optional)</description></listheader>
///   <item><term>Heading</term><description>Level 1–4; PlainText, not blank, at most 200.</description></item>
///   <item><term>Paragraph, TakeawayItem</term><description>Text.</description></item>
///   <item><term>Quote</term><description>Text; Author (optional, at most 200); Source (optional, at most 200).</description></item>
///   <item><term>List</term><description>Variant: exactly <c>Ordered</c> or <c>Unordered</c> (<see cref="ListStyle"/> member names,
///   case-sensitive; numeric strings such as <c>"1"</c> are rejected); Children.</description></item>
///   <item><term>ListItem</term><description>Text; Children (optional).</description></item>
///   <item><term>Checklist, KeyTakeaways, Gallery, TableRow, Transcript</term><description>Children only.</description></item>
///   <item><term>ChecklistItem</term><description>Text; IsChecked.</description></item>
///   <item><term>Callout</term><description>Variant: exactly one <see cref="CalloutVariant"/> member name (case-sensitive; numeric strings
///   are rejected); Title (optional, at most 200); Children.</description></item>
///   <item><term>Toggle</term><description>Title, not blank, at most 200; Children.</description></item>
///   <item><term>Divider</term><description>No fields.</description></item>
///   <item><term>Image</term><description>Url (HTTPS); AltText, not blank, at most 300; Caption (optional, at most 300); Credit (optional, at most 200).</description></item>
///   <item><term>Video, Audio</term><description>Url (HTTPS); Caption (optional, at most 300); DurationSeconds (optional, not negative).</description></item>
///   <item><term>Embed</term><description>Url (HTTPS, host in <see cref="AllowedEmbedHosts"/>); Caption (optional, at most 300).</description></item>
///   <item><term>LinkCard</term><description>Url (HTTPS); Title, not blank, at most 200; Description (optional, at most 500).</description></item>
///   <item><term>Code</term><description>PlainText, not blank, at most <see cref="MaxCodeLength"/>; Language (optional, at most 30).</description></item>
///   <item><term>Table</term><description>HasHeaderRow (optional); Children.</description></item>
///   <item><term>TableCell</term><description>Text (optional: a cell may be empty).</description></item>
///   <item><term>Timestamp</term><description>Text; AtSeconds, not negative.</description></item>
///   <item><term>TranscriptSegment</term><description>Text; AtSeconds, not negative; Speaker (optional, at most 100).</description></item>
/// </list>
/// <para><b>Text spans</b> of blocks with formatted text: at least one span (except TableCell), at most <see cref="MaxSpansPerBlock"/>;
/// each span non-empty and at most <see cref="MaxSpanLength"/> characters; its <see cref="SpanSpec.Marks"/> may combine only the flags
/// defined in <see cref="TextMarks"/> (an undefined bit, e.g. <c>(TextMarks)64</c>, is rejected); a link must be an HTTPS URL or a
/// <c>mailto:</c> address (at most <see cref="WebUrl.MaxLength"/> characters). URLs of blocks follow <see cref="WebUrl.IsHttps(string?)"/>. Text is never trimmed.</para>
/// <para><b>Derived values.</b> The plain text is built from heading and code texts and the concatenated spans of each block, one line per
/// block; reading time is the word count divided by <see cref="WordsPerMinute"/>, rounded up (see <see cref="BuiltContent"/>).</para>
/// <para>The error messages (reasons) are in Polish and meant for editors and developers; clients must branch on the code only.</para>
/// </remarks>
/// <example>
/// <code>
/// var result = ContentBuilder.Build([new BlockSpec(BlockType.ListItem, Text: [new SpanSpec("Orphan item")])]);
/// // result.IsFailure == true
/// // result.Error.Code == "knowledge.content.invalid_block"
/// // result.Error.Message starts with "blocks[0]: " (a ListItem is not allowed at the top level)
/// </code>
/// </example>
/// <seealso cref="Material.ReplaceContent"/>
/// <seealso cref="BlockSpec"/>
/// <seealso cref="BlockType"/>
public static class ContentBuilder
{
    /// <summary>Maximum number of blocks in the content of one material, counting nested blocks at every level.</summary>
    public const int MaxBlocks = 500;
    /// <summary>Maximum number of nested <see cref="BlockType.List"/> levels (a top-level list is level 1).</summary>
    public const int MaxListDepth = 3;
    /// <summary>Maximum number of nested <see cref="BlockType.Toggle"/> levels (a top-level toggle is level 1).</summary>
    public const int MaxToggleDepth = 2;
    /// <summary>Maximum number of formatted-text spans in one block.</summary>
    public const int MaxSpansPerBlock = 200;
    /// <summary>Maximum length of the text of one span, in characters.</summary>
    public const int MaxSpanLength = 5000;
    /// <summary>Maximum length of the source code of a <see cref="BlockType.Code"/> block, in characters.</summary>
    public const int MaxCodeLength = 20000;
    /// <summary>Assumed reading speed, in words per minute, used to compute <see cref="BuiltContent.ReadingTimeMinutes"/>.</summary>
    public const int WordsPerMinute = 200;

    /// <summary>
    /// Host names of the providers whose players may be embedded with <see cref="BlockType.Embed"/> (YouTube, Vimeo, Spotify);
    /// compared case-insensitively and exactly, so subdomains not listed here are rejected.
    /// </summary>
    /// <remarks>An allow list protects readers from arbitrary third-party frames. Adding a provider is a deliberate code change.</remarks>
    public static readonly IReadOnlySet<string> AllowedEmbedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "www.youtube.com", "youtube.com", "youtu.be", "player.vimeo.com", "vimeo.com", "open.spotify.com",
    };

    // Types allowed at the top level of the content; also the allowed children of Toggle (and of Callout, minus Callout itself).
    private static readonly BlockType[] TopLevel =
    [
        BlockType.Heading, BlockType.Paragraph, BlockType.Quote, BlockType.List, BlockType.Checklist, BlockType.Callout,
        BlockType.Toggle, BlockType.KeyTakeaways, BlockType.Divider, BlockType.Image, BlockType.Gallery, BlockType.Video,
        BlockType.Audio, BlockType.Embed, BlockType.LinkCard, BlockType.Code, BlockType.Table, BlockType.Timestamp,
        BlockType.Transcript,
    ];

    // Allowed child types per parent type; a type missing here cannot have children.
    private static readonly Dictionary<BlockType, BlockType[]> AllowedChildren = new()
    {
        [BlockType.List] = [BlockType.ListItem],
        [BlockType.ListItem] = [BlockType.List],
        [BlockType.Checklist] = [BlockType.ChecklistItem],
        [BlockType.KeyTakeaways] = [BlockType.TakeawayItem],
        [BlockType.Gallery] = [BlockType.Image],
        [BlockType.Table] = [BlockType.TableRow],
        [BlockType.TableRow] = [BlockType.TableCell],
        [BlockType.Transcript] = [BlockType.TranscriptSegment],
        [BlockType.Callout] = [.. TopLevel.Where(type => type != BlockType.Callout)],
        [BlockType.Toggle] = TopLevel,
    };

    // All flags defined in TextMarks; a span whose marks have any other bit set is rejected.
    private static readonly TextMarks DefinedMarks = Enum.GetValues<TextMarks>().Aggregate(TextMarks.None, (all, mark) => all | mark);

    // Types whose text is a list of spans (BlockSpec.Text); all of them except TableCell require at least one span.
    private static readonly HashSet<BlockType> RichTextTypes =
    [
        BlockType.Paragraph, BlockType.Quote, BlockType.ListItem, BlockType.ChecklistItem, BlockType.TakeawayItem,
        BlockType.TableCell, BlockType.Timestamp, BlockType.TranscriptSegment,
    ];

    /// <summary>
    /// Validates a block tree (allowed child types, nesting and block count limits, required and allowed fields per type, HTTPS URLs,
    /// text spans) and flattens it into <see cref="ContentBlock"/> nodes with new identifiers.
    /// </summary>
    /// <remarks>
    /// A pure function: it has no side effects and does not touch any aggregate; <see cref="Material.ReplaceContent"/> calls it and stores
    /// the result. See the type remarks for the complete rule set.
    /// </remarks>
    /// <param name="blocks">Top-level blocks, each with its nested blocks in <see cref="BlockSpec.Children"/>; an empty list yields empty content.</param>
    /// <returns>
    /// The built content (nodes in depth-first order, plain text and reading time), or a validation error <c>knowledge.content.invalid_block</c>
    /// describing the first violation, prefixed with the path to the offending block (e.g. <c>blocks[1].children[0]: ...</c>).
    /// </returns>
    public static Result<BuiltContent> Build(IReadOnlyList<BlockSpec> blocks)
    {
        var context = new BuildContext();
        var error = AddChildren(context, blocks, parent: null, parentType: null, path: "blocks", listDepth: 0, toggleDepth: 0);
        if (error is not null)
        {
            return error;
        }

        var plainText = string.Join('\n', context.Texts);
        var words = context.Texts.Sum(text => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        var readingTime = words == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(words / (double)WordsPerMinute));

        return new BuiltContent(context.Blocks, plainText, readingTime);
    }

    // Validates and appends the given sibling specs (and, recursively, their subtrees) to the context.
    // Returns the first violation found, or null when the whole subtree is valid.
    private static Error? AddChildren(
        BuildContext context,
        IReadOnlyList<BlockSpec> specs,
        ContentBlock? parent,
        BlockType? parentType,
        string path,
        int listDepth,
        int toggleDepth)
    {
        var allowed = parentType is { } type ? AllowedChildren.GetValueOrDefault(type, []) : TopLevel;

        for (var index = 0; index < specs.Count; index++)
        {
            var spec = specs[index];
            var blockPath = parentType is null ? $"{path}[{index}]" : $"{path}.children[{index}]";

            if (!allowed.Contains(spec.Type))
            {
                return Invalid(blockPath, $"blok {spec.Type} nie jest dozwolony w tym miejscu");
            }

            var nextListDepth = spec.Type == BlockType.List ? listDepth + 1 : listDepth;
            var nextToggleDepth = spec.Type == BlockType.Toggle ? toggleDepth + 1 : toggleDepth;
            if (nextListDepth > MaxListDepth)
            {
                return Invalid(blockPath, $"listy można zagnieżdżać najwyżej na {MaxListDepth} poziomach");
            }

            if (nextToggleDepth > MaxToggleDepth)
            {
                return Invalid(blockPath, $"bloki Toggle można zagnieżdżać najwyżej na {MaxToggleDepth} poziomach");
            }

            if (++context.Count > MaxBlocks)
            {
                return Invalid(blockPath, $"treść może zawierać najwyżej {MaxBlocks} bloków");
            }

            var fieldError = ValidateFields(spec) ?? ValidateSpans(spec) ?? ValidateChildrenCount(spec);
            if (fieldError is not null)
            {
                return Invalid(blockPath, fieldError);
            }

            var block = ContentBlock.Create(parent?.Id, index, spec);
            context.Blocks.Add(block);
            CollectText(context, spec);

            var childError = AddChildren(context, spec.Children ?? [], block, spec.Type, blockPath, nextListDepth, nextToggleDepth);
            if (childError is not null)
            {
                return childError;
            }
        }

        return null;
    }

    // Checks that only fields applicable to the type are set, then the type-specific required fields and formats.
    // The switch returns the first failing rule; the reason texts are part of the error message (kept in Polish).
    private static string? ValidateFields(BlockSpec spec)
    {
        var allowed = AllowedFields(spec.Type);
        var present = PresentFields(spec);
        var unexpected = present & ~allowed;
        if (unexpected != BlockField.None)
        {
            return $"pola {unexpected} nie dotyczą bloku {spec.Type}";
        }

        return spec.Type switch
        {
            BlockType.Heading when spec.Level is not (>= 1 and <= 4) => "poziom nagłówka musi mieć wartość 1–4",
            BlockType.Heading when !HasText(spec.PlainText, 200) => "nagłówek wymaga tekstu (najwyżej 200 znaków)",
            BlockType.List when !IsDefinedName<ListStyle>(spec.Variant) => "styl listy musi mieć wartość Ordered lub Unordered",
            BlockType.Callout when !IsDefinedName<CalloutVariant>(spec.Variant) => "wariant wyróżnienia musi mieć wartość Info, Tip, Warning lub Important",
            BlockType.Toggle when !HasText(spec.Title, 200) => "blok Toggle wymaga nagłówka (najwyżej 200 znaków)",
            BlockType.LinkCard when !HasText(spec.Title, 200) => "karta linku wymaga tytułu (najwyżej 200 znaków)",
            BlockType.Image when !HasText(spec.AltText, 300) => "obraz wymaga tekstu alternatywnego (najwyżej 300 znaków)",
            BlockType.Code when !HasText(spec.PlainText, MaxCodeLength) => $"blok kodu wymaga treści (najwyżej {MaxCodeLength} znaków)",
            BlockType.ChecklistItem when spec.IsChecked is null => "element listy kontrolnej wymaga stanu zaznaczenia",
            BlockType.Timestamp or BlockType.TranscriptSegment when spec.AtSeconds is not >= 0 => "znacznik czasu musi być nieujemny",
            BlockType.Image or BlockType.Video or BlockType.Audio or BlockType.LinkCard when !WebUrl.IsHttps(spec.Url) => "adres musi być poprawnym adresem https",
            BlockType.Embed when !WebUrl.IsHttps(spec.Url) || !AllowedEmbedHosts.Contains(new Uri(spec.Url!).Host) => "osadzenie dozwolone wyłącznie od dozwolonych dostawców (https)",
            _ when spec.DurationSeconds is < 0 => "czas trwania nie może być ujemny",
            _ when !OptionalLength(spec) => "pole tekstowe przekracza dozwoloną długość",
            _ => null,
        };
    }

    // Checks the span list of formatted-text blocks: presence, count, text length, defined marks and link scheme.
    private static string? ValidateSpans(BlockSpec spec)
    {
        var spans = spec.Text ?? [];
        if (!RichTextTypes.Contains(spec.Type))
        {
            return null;
        }

        if (spans.Count == 0 && spec.Type != BlockType.TableCell)
        {
            return "blok wymaga tekstu";
        }

        if (spans.Count > MaxSpansPerBlock)
        {
            return $"blok może zawierać najwyżej {MaxSpansPerBlock} fragmentów tekstu";
        }

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text) || span.Text.Length > MaxSpanLength)
            {
                return $"fragment tekstu musi być niepusty i mieć najwyżej {MaxSpanLength} znaków";
            }

            if ((span.Marks & ~DefinedMarks) != TextMarks.None)
            {
                return "formatowanie tekstu zawiera nieznane oznaczenia";
            }

            if (span.LinkHref is not null && !IsAllowedLink(span.LinkHref))
            {
                return "link w tekście musi być adresem https lub mailto";
            }
        }

        return null;
    }

    // Checks the number of children (and equal cell count of table rows); the child types themselves are checked by AddChildren.
    private static string? ValidateChildrenCount(BlockSpec spec)
    {
        var children = spec.Children ?? [];
        return spec.Type switch
        {
            BlockType.List or BlockType.Checklist or BlockType.KeyTakeaways or BlockType.Transcript when children.Count == 0
                => "blok wymaga co najmniej jednego elementu",
            BlockType.Gallery when children.Count is < 2 or > 12 => "galeria wymaga od 2 do 12 obrazów",
            BlockType.Table when children.Count is < 1 or > 20 => "tabela wymaga od 1 do 20 wierszy",
            BlockType.Table when children.Select(row => row.Children?.Count ?? 0).Distinct().Count() > 1
                => "wszystkie wiersze tabeli muszą mieć tę samą liczbę komórek",
            BlockType.TableRow when children.Count is < 1 or > 10 => "wiersz tabeli wymaga od 1 do 10 komórek",
            BlockType.Callout or BlockType.Toggle when children.Count == 0 => "blok wymaga treści",
            _ => null,
        };
    }

    // Fields a block of the given type may carry. Children is listed exactly for the types that have an entry in AllowedChildren,
    // so children of any other type (e.g. Paragraph) are reported as a field foreign to the type.
    private static BlockField AllowedFields(BlockType type) => type switch
    {
        BlockType.Heading => BlockField.Level | BlockField.PlainText,
        BlockType.Paragraph or BlockType.TakeawayItem or BlockType.TableCell => BlockField.Text,
        BlockType.ListItem => BlockField.Text | BlockField.Children,
        BlockType.Quote => BlockField.Text | BlockField.Author | BlockField.Source,
        BlockType.List => BlockField.Variant | BlockField.Children,
        BlockType.Checklist or BlockType.KeyTakeaways or BlockType.Gallery or BlockType.TableRow or BlockType.Transcript => BlockField.Children,
        BlockType.ChecklistItem => BlockField.Text | BlockField.IsChecked,
        BlockType.Callout => BlockField.Variant | BlockField.Title | BlockField.Children,
        BlockType.Toggle => BlockField.Title | BlockField.Children,
        BlockType.Divider => BlockField.None,
        BlockType.Image => BlockField.Url | BlockField.AltText | BlockField.Caption | BlockField.Credit,
        BlockType.Video or BlockType.Audio => BlockField.Url | BlockField.Caption | BlockField.DurationSeconds,
        BlockType.Embed => BlockField.Url | BlockField.Caption,
        BlockType.LinkCard => BlockField.Url | BlockField.Title | BlockField.Description,
        BlockType.Code => BlockField.PlainText | BlockField.Language,
        BlockType.Table => BlockField.HasHeaderRow | BlockField.Children,
        BlockType.Timestamp => BlockField.Text | BlockField.AtSeconds,
        BlockType.TranscriptSegment => BlockField.Text | BlockField.AtSeconds | BlockField.Speaker,
        _ => BlockField.None,
    };

    // Fields that are set on the spec: non-null values, and non-empty lists for Children and Text.
    private static BlockField PresentFields(BlockSpec spec)
    {
        var fields = BlockField.None;
        fields |= spec.Children is { Count: > 0 } ? BlockField.Children : 0;
        fields |= spec.Text is { Count: > 0 } ? BlockField.Text : 0;
        fields |= spec.Level is not null ? BlockField.Level : 0;
        fields |= spec.Variant is not null ? BlockField.Variant : 0;
        fields |= spec.Title is not null ? BlockField.Title : 0;
        fields |= spec.PlainText is not null ? BlockField.PlainText : 0;
        fields |= spec.Url is not null ? BlockField.Url : 0;
        fields |= spec.AltText is not null ? BlockField.AltText : 0;
        fields |= spec.Caption is not null ? BlockField.Caption : 0;
        fields |= spec.Credit is not null ? BlockField.Credit : 0;
        fields |= spec.Language is not null ? BlockField.Language : 0;
        fields |= spec.DurationSeconds is not null ? BlockField.DurationSeconds : 0;
        fields |= spec.AtSeconds is not null ? BlockField.AtSeconds : 0;
        fields |= spec.Speaker is not null ? BlockField.Speaker : 0;
        fields |= spec.IsChecked is not null ? BlockField.IsChecked : 0;
        fields |= spec.HasHeaderRow is not null ? BlockField.HasHeaderRow : 0;
        fields |= spec.Author is not null ? BlockField.Author : 0;
        fields |= spec.Source is not null ? BlockField.Source : 0;
        fields |= spec.Description is not null ? BlockField.Description : 0;
        return fields;
    }

    // Maximum lengths of the optional text attributes; they match the column sizes of the ContentBlocks table.
    private static bool OptionalLength(BlockSpec spec) =>
        Fits(spec.Caption, 300) && Fits(spec.Credit, 200) && Fits(spec.Title, 200) && Fits(spec.Language, 30)
        && Fits(spec.Speaker, 100) && Fits(spec.Author, 200) && Fits(spec.Source, 200) && Fits(spec.Description, 500);

    private static bool Fits(string? value, int maxLength) => value is null || value.Length <= maxLength;

    // Exact, case-sensitive match with a member name of the enum. Enum.TryParse is not used on purpose: it also accepts numeric strings
    // ("7"), undefined values and comma-separated lists, which would pass here and then violate the CHECK constraints of the database.
    private static bool IsDefinedName<TEnum>(string? value)
        where TEnum : struct, Enum =>
        value is not null && Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal);

    private static bool HasText(string? value, int maxLength) => !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;

    private static bool IsAllowedLink(string href) =>
        WebUrl.IsHttps(href)
        || (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && href.Length is > 7 and <= WebUrl.MaxLength);

    // Adds the searchable text of one block (heading or code text, then its concatenated spans) to the plain-text lines.
    private static void CollectText(BuildContext context, BlockSpec spec)
    {
        if (spec.Type is BlockType.Heading or BlockType.Code && spec.PlainText is not null)
        {
            context.Texts.Add(spec.PlainText);
        }

        if (spec.Text is { Count: > 0 } spans)
        {
            context.Texts.Add(string.Concat(spans.Select(span => span.Text)));
        }
    }

    private static Error Invalid(string path, string reason) =>
        Error.Validation("knowledge.content.invalid_block", $"{path}: {reason}.");

    // Mutable state of one Build call: produced nodes in depth-first order, plain-text lines and the running block count.
    private sealed class BuildContext
    {
        public List<ContentBlock> Blocks { get; } = [];

        public List<string> Texts { get; } = [];

        public int Count { get; set; }
    }

    // Bit set of the optional BlockSpec fields, used to compare the fields present on a spec with those allowed for its type.
    [Flags]
    private enum BlockField
    {
        None = 0,
        Children = 1 << 0,
        Text = 1 << 1,
        Level = 1 << 2,
        Variant = 1 << 3,
        Title = 1 << 4,
        PlainText = 1 << 5,
        Url = 1 << 6,
        AltText = 1 << 7,
        Caption = 1 << 8,
        Credit = 1 << 9,
        Language = 1 << 10,
        DurationSeconds = 1 << 11,
        AtSeconds = 1 << 12,
        Speaker = 1 << 13,
        IsChecked = 1 << 14,
        HasHeaderRow = 1 << 15,
        Author = 1 << 16,
        Source = 1 << 17,
        Description = 1 << 18,
    }
}
