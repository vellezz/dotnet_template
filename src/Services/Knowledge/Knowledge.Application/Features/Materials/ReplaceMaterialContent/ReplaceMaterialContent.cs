using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using Knowledge.Application.Content.Blocks;
using Knowledge.Application.Content;

namespace Knowledge.Application.Features.Materials.ReplaceMaterialContent;

/// <summary>
/// Replaces the whole content of a material with a new tree of blocks. Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Content is always saved as a whole (ADR-0028); there are no commands for single blocks. To change one paragraph, send the full content
/// with that paragraph changed. The JSON shape of every block type is described on <see cref="ContentBlockDto"/>.
/// </para>
/// <para>Input rules (<c>ReplaceMaterialContentValidator</c>): <see cref="MaterialId"/> not empty; <see cref="Blocks"/> present (may be empty).</para>
/// <para>
/// The handler loads the material, converts the blocks with <see cref="ContentMapper.ToSpecs"/> and calls <c>Material.ReplaceContent</c>.
/// The aggregate validates the whole tree (allowed block types and nesting, text and URL rules, at most
/// <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.MaxBlocks"/> blocks counting nested blocks and items), stops at the first
/// violation, and on success replaces all stored blocks and recomputes the plain text and reading time. Allowed for drafts and published
/// materials; a published material is updated for readers immediately.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.NotFound"/> (<c>knowledge.material.not_found</c>, 404).</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.Archived"/> (<c>knowledge.material.archived</c>, 422).</description></item>
///   <item><description><c>knowledge.content.invalid_block</c> (400): a block breaks a content rule; the message starts with the path of the
///   block, for example <c>blocks[2].children[0]: ...</c>.</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.ContentRequired"/> (<c>knowledge.material.content_required</c>, 422):
///   empty content for a published material.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// await sender.Send(
///     new ReplaceMaterialContent(materialId, [
///         new HeadingBlockDto(2, "Evening routine"),
///         new ParagraphBlockDto([new SpanDto("Keep the room "), new SpanDto("dark", [TextMarks.Bold])]),
///     ]),
///     cancellationToken);
/// </code>
/// </example>
/// <param name="MaterialId">Identifier of the material; must not be <see cref="Guid.Empty"/>.</param>
/// <param name="Blocks">
/// The complete new content as top-level blocks in display order, with nested blocks inside them. Must not be <see langword="null"/>;
/// an empty list clears the content (drafts only).
/// </param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record ReplaceMaterialContent(Guid MaterialId, IReadOnlyList<ContentBlockDto> Blocks) : ICommand;
