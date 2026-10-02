using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Categories.RenameCategory;

/// <summary>
/// Changes the display name of an existing category; the slug stays the same. Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Input rules (<c>RenameCategoryValidator</c>): <see cref="CategoryId"/> not empty; <see cref="Name"/> not blank, at most
/// <see cref="Knowledge.Domain.Categories.Category.MaxNameLength"/> characters after trimming.
/// </para>
/// <para>
/// The handler loads the category and calls <c>Category.Rename</c>, which trims the name and raises <c>CategoryChanged</c>
/// (invalidating the cached category list after the commit). Renaming to the current name (after trimming) is an idempotent success
/// that changes nothing. Names do not have to be unique.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
///   <item><description><see cref="Knowledge.Domain.Categories.CategoryErrors.NotFound"/> (<c>knowledge.category.not_found</c>, 404):
///   no category with this identifier.</description></item>
/// </list>
/// </remarks>
/// <param name="CategoryId">Identifier of the category to rename; must not be <see cref="Guid.Empty"/>.</param>
/// <param name="Name">New display name; not blank, at most <see cref="Knowledge.Domain.Categories.Category.MaxNameLength"/> characters after trimming. Stored trimmed.</param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record RenameCategory(Guid CategoryId, string Name) : ICommand;
