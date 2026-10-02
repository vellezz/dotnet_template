using Knowledge.Application.Content.Blocks;
using Knowledge.Infrastructure.Persistence.Read.Models;
using Knowledge.Application.Content;
using Knowledge.Domain.Materials.Content;
using Knowledge.Infrastructure.Persistence.Read;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Features.Materials;

/// <summary>
/// Loads the block content of one material from the <c>ContentBlocks</c> and <c>ContentTextSpans</c> tables and assembles it into the
/// tree of <see cref="ContentBlockDto"/> returned by the API (ADR-0028).
/// </summary>
/// <remarks>
/// <para>
/// The content is stored relationally: one row per block (with <c>ParentBlockId</c> and <c>Position</c> among its siblings) and one row
/// per formatted text span. The reader runs exactly two queries (all blocks of the material, then all their spans), groups the rows in
/// memory by parent and orders them by position, and converts the tree back to DTOs with the same <see cref="ContentMapper"/> that
/// converts incoming DTOs, so the content returned is identical to the content that was saved.
/// </para>
/// <para>Used by <see cref="GetMaterialHandler"/>.</para>
/// </remarks>
internal static class ContentTreeReader
{
    /// <summary>Reads the complete content tree of a material.</summary>
    /// <param name="db">Read context of the service.</param>
    /// <param name="materialId">Identifier of the material; its status is not checked here.</param>
    /// <param name="cancellationToken">Cancellation of the request.</param>
    /// <returns>
    /// Top-level blocks in display order with their children, or an empty list when the material has no content (or does not exist).
    /// </returns>
    public static async Task<IReadOnlyList<ContentBlockDto>> ReadAsync(KnowledgeReadDbContext db, Guid materialId, CancellationToken cancellationToken)
    {
        var blocks = await db.ContentBlocks.Where(block => block.MaterialId == materialId).ToListAsync(cancellationToken);
        if (blocks.Count == 0)
        {
            return [];
        }

        var spans = await db.ContentTextSpans
            .Where(span => db.ContentBlocks.Any(block => block.MaterialId == materialId && block.Id == span.BlockId))
            .ToListAsync(cancellationToken);

        var spansByBlock = spans
            .GroupBy(span => span.BlockId)
            .ToDictionary(group => group.Key, group => group.OrderBy(span => span.Position).ToList());
        var childrenByParent = blocks
            .GroupBy(block => block.ParentBlockId ?? Guid.Empty)
            .ToDictionary(group => group.Key, group => group.OrderBy(block => block.Position).ToList());

        IReadOnlyList<BlockSpec> Build(Guid parentId) =>
            childrenByParent.TryGetValue(parentId, out var children)
                ? children.Select(block => ToSpec(block, Build(block.Id), spansByBlock.GetValueOrDefault(block.Id))).ToList()
                : [];

        return ContentMapper.ToDtos(Build(Guid.Empty));
    }

    // Maps a row back to the domain block specification. Type and Marks were written from the same enums, so parsing cannot fail
    // for data written by the application.
    private static BlockSpec ToSpec(ContentBlockRow row, IReadOnlyList<BlockSpec> children, List<ContentTextSpanRow>? spans) => new(
        Enum.Parse<BlockType>(row.Type),
        Children: children,
        Text: spans?.Select(span => new SpanSpec(span.Text, (TextMarks)span.Marks, span.LinkHref)).ToList(),
        Level: row.Level,
        Variant: row.Variant,
        Title: row.Title,
        PlainText: row.PlainText,
        Url: row.Url,
        AltText: row.AltText,
        Caption: row.Caption,
        Credit: row.Credit,
        Language: row.Language,
        DurationSeconds: row.DurationSeconds,
        AtSeconds: row.AtSeconds,
        Speaker: row.Speaker,
        IsChecked: row.IsChecked,
        HasHeaderRow: row.HasHeaderRow,
        Author: row.Author,
        Source: row.Source,
        Description: row.Description);
}
