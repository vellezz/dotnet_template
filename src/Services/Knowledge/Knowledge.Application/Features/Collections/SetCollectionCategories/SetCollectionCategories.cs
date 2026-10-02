using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Collections.SetCollectionCategories;

/// <summary>
/// Replaces the set of categories of a collection with the given set. Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Input rules (<c>SetCollectionCategoriesValidator</c>): <see cref="CollectionId"/> not empty; <see cref="CategoryIds"/> present
/// (not <see langword="null"/>) and none of its items empty.
/// </para>
/// <para>
/// The handler loads the collection, removes duplicate identifiers, checks that every category exists and calls
/// <c>Collection.SetCategories</c>. The whole set is replaced (not merged); order does not matter. Allowed in any status except archived,
/// including published collections.
/// </para>
/// <para>Result: success without a value. Possible errors, in the order they are checked:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.NotFound"/> (<c>knowledge.collection.not_found</c>, 404).</description></item>
///   <item><description><see cref="Knowledge.Domain.Categories.CategoryErrors.UnknownCategories"/> (<c>knowledge.category.unknown</c>, 400):
///   at least one category does not exist.</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.Archived"/> (<c>knowledge.collection.archived</c>, 422).</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.TooManyCategories"/>
///   (<c>knowledge.collection.too_many_categories</c>, 400): more than <see cref="Knowledge.Domain.Collections.Collection.MaxCategories"/>
///   distinct categories.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// await sender.Send(new SetCollectionCategories(collectionId, [sleepCategoryId, stressCategoryId]), cancellationToken);
/// </code>
/// </example>
/// <param name="CollectionId">Identifier of the collection; must not be <see cref="Guid.Empty"/>.</param>
/// <param name="CategoryIds">
/// The complete new set of category identifiers; at most <see cref="Knowledge.Domain.Collections.Collection.MaxCategories"/> distinct
/// values, duplicates are ignored. An empty list removes all categories.
/// </param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record SetCollectionCategories(Guid CollectionId, IReadOnlyList<Guid> CategoryIds) : ICommand;
