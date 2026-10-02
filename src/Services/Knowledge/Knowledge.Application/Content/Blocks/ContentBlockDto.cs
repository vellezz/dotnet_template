using System.Text.Json.Serialization;

namespace Knowledge.Application.Content.Blocks;

/// <summary>
/// Base type of one block of material content in the HTTP contract (for example a heading, a paragraph or an image).
/// The content of a material is an ordered list of such blocks; some blocks contain nested blocks, forming a tree.
/// </summary>
/// <remarks>
/// <para>
/// In JSON every block is an object with a <c>type</c> discriminator property followed by the fields of that block type
/// (camelCase names; enum values as their C# names, for example <c>"Info"</c>). The OpenAPI 3.0 contract describes the block
/// as <c>anyOf</c> with a <c>discriminator</c> on <c>type</c> (ADR-0028). Discriminator values:
/// </para>
/// <list type="table">
///   <listheader><term><c>type</c></term><description>DTO</description></listheader>
///   <item><term><c>heading</c></term><description><see cref="HeadingBlockDto"/></description></item>
///   <item><term><c>paragraph</c></term><description><see cref="ParagraphBlockDto"/></description></item>
///   <item><term><c>quote</c></term><description><see cref="QuoteBlockDto"/></description></item>
///   <item><term><c>list</c></term><description><see cref="ListBlockDto"/></description></item>
///   <item><term><c>checklist</c></term><description><see cref="ChecklistBlockDto"/></description></item>
///   <item><term><c>callout</c></term><description><see cref="CalloutBlockDto"/></description></item>
///   <item><term><c>toggle</c></term><description><see cref="ToggleBlockDto"/></description></item>
///   <item><term><c>keyTakeaways</c></term><description><see cref="KeyTakeawaysBlockDto"/></description></item>
///   <item><term><c>divider</c></term><description><see cref="DividerBlockDto"/></description></item>
///   <item><term><c>image</c></term><description><see cref="ImageBlockDto"/></description></item>
///   <item><term><c>gallery</c></term><description><see cref="GalleryBlockDto"/></description></item>
///   <item><term><c>video</c></term><description><see cref="VideoBlockDto"/></description></item>
///   <item><term><c>audio</c></term><description><see cref="AudioBlockDto"/></description></item>
///   <item><term><c>embed</c></term><description><see cref="EmbedBlockDto"/></description></item>
///   <item><term><c>linkCard</c></term><description><see cref="LinkCardBlockDto"/></description></item>
///   <item><term><c>code</c></term><description><see cref="CodeBlockDto"/></description></item>
///   <item><term><c>table</c></term><description><see cref="TableBlockDto"/></description></item>
///   <item><term><c>timestamp</c></term><description><see cref="TimestampBlockDto"/></description></item>
///   <item><term><c>transcript</c></term><description><see cref="TranscriptBlockDto"/></description></item>
/// </list>
/// <para>
/// With the default <c>System.Text.Json</c> settings <c>type</c> must be the first property of the object; a missing or unknown
/// discriminator makes JSON deserialization fail, so the request never reaches the command.
/// </para>
/// <para>
/// These DTOs carry data only. All content rules (https URLs, allowed embed providers, required alternative text, text length limits,
/// allowed nesting, at most <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxBlocks"/> blocks) are checked by the
/// <c>Material</c> aggregate when the content is saved; a violation is returned as <c>knowledge.content.invalid_block</c>
/// (HTTP 400) with a message that starts with the path of the offending block, for example <c>blocks[3].children[0]: ...</c>.
/// <see cref="ContentMapper"/> converts between these DTOs and the domain's <c>BlockSpec</c> tree.
/// </para>
/// <para>
/// Items of composite blocks (<see cref="ListItemDto"/>, <see cref="ChecklistItemDto"/>, <see cref="TakeawayItemDto"/>,
/// <see cref="TableRowDto"/>, <see cref="TableCellDto"/>, <see cref="TranscriptSegmentDto"/>) are not blocks and have no <c>type</c>.
/// Formatted text is a list of <see cref="SpanDto"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [
///   { "type": "heading", "level": 2, "text": "Evening routine" },
///   { "type": "paragraph", "text": [ { "text": "Keep the room " }, { "text": "dark", "marks": [ "Bold" ] } ] },
///   { "type": "callout", "variant": "Tip", "title": "Remember", "blocks": [
///       { "type": "paragraph", "text": [ { "text": "No screens an hour before bed." } ] } ] },
///   { "type": "divider" }
/// ]
/// </code>
/// </example>
/// <seealso cref="ContentMapper"/>
/// <seealso cref="SpanDto"/>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HeadingBlockDto), "heading")]
[JsonDerivedType(typeof(ParagraphBlockDto), "paragraph")]
[JsonDerivedType(typeof(QuoteBlockDto), "quote")]
[JsonDerivedType(typeof(ListBlockDto), "list")]
[JsonDerivedType(typeof(ChecklistBlockDto), "checklist")]
[JsonDerivedType(typeof(CalloutBlockDto), "callout")]
[JsonDerivedType(typeof(ToggleBlockDto), "toggle")]
[JsonDerivedType(typeof(KeyTakeawaysBlockDto), "keyTakeaways")]
[JsonDerivedType(typeof(DividerBlockDto), "divider")]
[JsonDerivedType(typeof(ImageBlockDto), "image")]
[JsonDerivedType(typeof(GalleryBlockDto), "gallery")]
[JsonDerivedType(typeof(VideoBlockDto), "video")]
[JsonDerivedType(typeof(AudioBlockDto), "audio")]
[JsonDerivedType(typeof(EmbedBlockDto), "embed")]
[JsonDerivedType(typeof(LinkCardBlockDto), "linkCard")]
[JsonDerivedType(typeof(CodeBlockDto), "code")]
[JsonDerivedType(typeof(TableBlockDto), "table")]
[JsonDerivedType(typeof(TimestampBlockDto), "timestamp")]
[JsonDerivedType(typeof(TranscriptBlockDto), "transcript")]
public abstract record ContentBlockDto;
