using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Collections.SetCollectionItems;

/// <summary>
/// Sets the materials of a collection and their order, replacing the previous list. Requires scope
/// <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Input rules (<c>SetCollectionItemsValidator</c>): <see cref="CollectionId"/> not empty; <see cref="MaterialIds"/> present
/// (not <see langword="null"/>) and none of its items empty.
/// </para>
/// <para>
/// The handler loads the collection, checks that every referenced material exists (in any status: drafts and archived materials are
/// accepted) and calls <c>Collection.SetItems</c>, which stores the materials at positions 0, 1, 2... in list order. To reorder, send
/// the full list in the new order. A published collection cannot be emptied.
/// </para>
/// <para>Result: success without a value. Possible errors, in the order they are checked:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.NotFound"/> (<c>knowledge.collection.not_found</c>, 404).</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.UnknownMaterials"/>
///   (<c>knowledge.collection.unknown_materials</c>, 400): at least one material does not exist.</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.Archived"/> (<c>knowledge.collection.archived</c>, 422).</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.DuplicateItems"/>
///   (<c>knowledge.collection.duplicate_items</c>, 400): a material appears more than once.</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.TooManyItems"/> (<c>knowledge.collection.too_many_items</c>, 400):
///   more than <see cref="Knowledge.Domain.Collections.Collection.MaxItems"/> materials.</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.ItemsRequired"/>
///   (<c>knowledge.collection.items_required</c>, 422): an empty list for a published collection.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// await sender.Send(new SetCollectionItems(collectionId, [introMaterialId, routineMaterialId]), cancellationToken);
/// </code>
/// </example>
/// <param name="CollectionId">Identifier of the collection; must not be <see cref="Guid.Empty"/>.</param>
/// <param name="MaterialIds">
/// Material identifiers in the desired order (the index is the position); at most
/// <see cref="Knowledge.Domain.Collections.Collection.MaxItems"/> items, no duplicates. An empty list removes all materials (drafts only).
/// </param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record SetCollectionItems(Guid CollectionId, IReadOnlyList<Guid> MaterialIds) : ICommand;
