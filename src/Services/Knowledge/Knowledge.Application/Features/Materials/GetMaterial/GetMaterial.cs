using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;

namespace Knowledge.Application.Features.Materials.GetMaterial;

/// <summary>
/// Returns one material with its full content. Requires scope <see cref="KnowledgeScopes.CatalogRead"/>.
/// </summary>
/// <remarks>
/// <para>
/// Visibility depends on the caller. A reader sees only a published material. A caller that also holds
/// <see cref="KnowledgeScopes.CatalogWrite"/> (an editor) sees the material in any status (draft, published, archived), for example to
/// preview a draft.
/// </para>
/// <para>
/// The handler lives in Infrastructure (<c>GetMaterialHandler</c>, ADR-0026). It loads the material row, its categories, and the content
/// blocks and text spans (two queries) and assembles the block tree (ADR-0028). Published materials are cached for readers; the cache
/// entry is invalidated on every change of the material. Editor reads bypass the cache.
/// </para>
/// <para>Result: <see cref="MaterialDetailsDto"/>. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.NotFound"/> (<c>knowledge.material.not_found</c>, 404):
///   the material does not exist or is not visible to the caller (readers cannot tell a draft from a missing material).</description></item>
/// </list>
/// </remarks>
/// <param name="MaterialId">Identifier of the material.</param>
[RequiresScope(KnowledgeScopes.CatalogRead)]
public sealed record GetMaterial(Guid MaterialId) : IQuery<Result<MaterialDetailsDto>>;
