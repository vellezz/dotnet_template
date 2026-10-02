using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;

namespace Knowledge.Application.Features.Categories.CreateCategory;

/// <summary>
/// Creates a catalog category with a display name and a unique slug. Categories are flat (no hierarchy) and are used to group
/// materials and collections. Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Input rules (<c>CreateCategoryValidator</c>): <see cref="Name"/> not blank, at most
/// <see cref="Knowledge.Domain.Categories.Category.MaxNameLength"/> characters after trimming; <see cref="Slug"/> not blank, at most
/// <see cref="Knowledge.Domain.Categories.Category.MaxSlugLength"/> characters. The slug format is checked by the aggregate.
/// </para>
/// <para>
/// The handler rejects a slug that is already used, then creates the <c>Category</c> aggregate (which trims the name and raises
/// <c>CategoryChanged</c>, invalidating the cached category list) and adds it to the repository. The transaction behavior saves it.
/// </para>
/// <para>Result: on success the identifier of the new category. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403): no authenticated caller; <c>auth.missing_scope</c> (403): the token lacks
///   <see cref="KnowledgeScopes.CatalogWrite"/>.</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above; field messages are in <see cref="Error.Details"/>.</description></item>
///   <item><description><see cref="Knowledge.Domain.Categories.CategoryErrors.InvalidSlug"/> (<c>knowledge.category.invalid_slug</c>, 400):
///   the slug is not made of lowercase letters, digits and single hyphens.</description></item>
///   <item><description><see cref="Knowledge.Domain.Categories.CategoryErrors.SlugTaken"/> (<c>knowledge.category.slug_taken</c>, 409):
///   another category already uses the slug. Also returned when two concurrent requests create the same slug and the unique index rejects
///   the second one.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// Result&lt;Guid&gt; result = await sender.Send(new CreateCategory("Healthy sleep", "healthy-sleep"), cancellationToken);
/// </code>
/// </example>
/// <param name="Name">Display name of the category; not blank, at most <see cref="Knowledge.Domain.Categories.Category.MaxNameLength"/> characters after trimming. Stored trimmed.</param>
/// <param name="Slug">
/// Unique identifier of the category for URLs; at most <see cref="Knowledge.Domain.Categories.Category.MaxSlugLength"/> characters,
/// only lowercase letters <c>a-z</c>, digits and single hyphens between them (for example <c>healthy-sleep</c>). Cannot be changed later.
/// </param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record CreateCategory(string Name, string Slug) : ICommand<Result<Guid>>;
