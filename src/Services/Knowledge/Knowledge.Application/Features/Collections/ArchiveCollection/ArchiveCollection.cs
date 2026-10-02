using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Collections.ArchiveCollection;

/// <summary>
/// Archives a collection: it disappears from the reader catalog and can no longer be changed. Archiving cannot be undone.
/// Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>Input rules (<c>ArchiveCollectionValidator</c>): <see cref="CollectionId"/> not empty.</para>
/// <para>
/// The handler loads the collection and calls <c>Collection.Archive</c>. The first archiving raises the <c>CollectionArchived</c> domain
/// event, which <c>CollectionArchivedTranslator</c> turns into the <c>CollectionArchivedV1</c> integration event (written to the outbox in
/// the same transaction). The Worker consumes it and removes all users' favorites of the collection (<c>RemoveFavoritesOfItem</c>).
/// The operation is idempotent: archiving an archived collection succeeds and publishes nothing.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): empty identifier.</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.NotFound"/> (<c>knowledge.collection.not_found</c>, 404).</description></item>
/// </list>
/// </remarks>
/// <param name="CollectionId">Identifier of the collection to archive; must not be <see cref="Guid.Empty"/>.</param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record ArchiveCollection(Guid CollectionId) : ICommand;
