using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Collections.PublishCollection;

/// <summary>
/// Publishes a draft collection so that readers can see it. Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>Input rules (<c>PublishCollectionValidator</c>): <see cref="CollectionId"/> not empty.</para>
/// <para>
/// The handler loads the collection and calls <c>Collection.Publish</c>, which requires at least one material and sets
/// <c>PublishedAt</c>. The operation is idempotent: publishing a published collection succeeds and keeps the original
/// <c>PublishedAt</c>. The materials themselves do not have to be published; readers simply do not see unpublished ones.
/// No integration event is published for collections.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): empty identifier.</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.NotFound"/> (<c>knowledge.collection.not_found</c>, 404).</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.Archived"/> (<c>knowledge.collection.archived</c>, 422):
///   archived collections cannot be published again.</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.ItemsRequired"/>
///   (<c>knowledge.collection.items_required</c>, 422): the collection has no materials.</description></item>
/// </list>
/// </remarks>
/// <param name="CollectionId">Identifier of the collection to publish; must not be <see cref="Guid.Empty"/>.</param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record PublishCollection(Guid CollectionId) : ICommand;
