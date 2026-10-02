namespace Knowledge.Domain.Materials.Content;

/// <summary>
/// Result of a successful <see cref="ContentBuilder.Build"/>: validated content nodes ready to be stored in a <see cref="Material"/>,
/// plus the values derived from them.
/// </summary>
/// <remarks>
/// Consumed by <see cref="Material.ReplaceContent"/>, which copies all three values into the aggregate. It has no other use; do not
/// build it by hand, because only <see cref="ContentBuilder"/> guarantees that the blocks are valid.
/// </remarks>
/// <param name="Blocks">
/// Flattened block tree in depth-first order (every parent before its children), linked through <see cref="ContentBlock.ParentBlockId"/>,
/// with fresh <see cref="Knowledge.Domain.Materials.Content.BlockId"/>s. Empty when the input was empty.
/// </param>
/// <param name="PlainText">
/// Plain text of the content: the text of headings and code blocks and the concatenated spans of every formatted-text block, one entry per
/// block in tree order, joined with <c>'\n'</c>. Titles, captions, alt texts and other attributes are not included.
/// </param>
/// <param name="ReadingTimeMinutes">
/// Estimated reading time in whole minutes: the number of white-space separated words in <paramref name="PlainText"/> divided by
/// <see cref="ContentBuilder.WordsPerMinute"/>, rounded up; 0 when there are no words, otherwise at least 1.
/// </param>
public sealed record BuiltContent(IReadOnlyList<ContentBlock> Blocks, string PlainText, int ReadingTimeMinutes);
