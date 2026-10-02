using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;

namespace Knowledge.Application.Features.Collections.CreateCollection;

/// <summary>
/// Creates a collection (an ordered, curated list of materials) as a draft without materials and categories.
/// Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Input rules (<c>CreateCollectionValidator</c>): <see cref="Title"/> not blank, at most
/// <see cref="Knowledge.Domain.Collections.Collection.MaxTitleLength"/> characters after trimming; <see cref="Description"/> at most
/// <see cref="Knowledge.Domain.Collections.Collection.MaxDescriptionLength"/> characters after trimming.
/// </para>
/// <para>
/// The handler creates the <c>Collection</c> aggregate in status <c>Draft</c> and adds it to the repository. Typical next steps:
/// <c>SetCollectionItems</c>, <c>SetCollectionCategories</c>, then <c>PublishCollection</c>. Drafts are visible only to editors.
/// </para>
/// <para>Result: on success the identifier of the new collection. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
/// </list>
/// </remarks>
/// <param name="Title">Title of the collection; not blank, at most <see cref="Knowledge.Domain.Collections.Collection.MaxTitleLength"/> characters after trimming. Stored trimmed.</param>
/// <param name="Description">
/// Optional description, at most <see cref="Knowledge.Domain.Collections.Collection.MaxDescriptionLength"/> characters after trimming;
/// <see langword="null"/> or blank means no description. Stored trimmed.
/// </param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record CreateCollection(string Title, string? Description) : ICommand<Result<Guid>>;
