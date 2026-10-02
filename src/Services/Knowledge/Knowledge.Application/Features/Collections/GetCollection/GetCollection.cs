using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;

namespace Knowledge.Application.Features.Collections.GetCollection;

/// <summary>
/// Returns one collection with its materials in the curated order. Requires scope <see cref="KnowledgeScopes.CatalogRead"/>.
/// </summary>
/// <remarks>
/// <para>
/// Visibility depends on the caller. A reader sees only a published collection and only its published materials. A caller that also
/// holds <see cref="KnowledgeScopes.CatalogWrite"/> (an editor) sees the collection in any status (draft, published, archived) and all
/// of its materials.
/// </para>
/// <para>The handler lives in Infrastructure (<c>GetCollectionHandler</c>, ADR-0026) and reads the read model without caching.</para>
/// <para>Result: <see cref="CollectionDetailsDto"/>. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.NotFound"/> (<c>knowledge.collection.not_found</c>, 404):
///   the collection does not exist or is not visible to the caller (readers cannot tell a draft from a missing collection).</description></item>
/// </list>
/// </remarks>
/// <param name="CollectionId">Identifier of the collection.</param>
[RequiresScope(KnowledgeScopes.CatalogRead)]
public sealed record GetCollection(Guid CollectionId) : IQuery<Result<CollectionDetailsDto>>;
