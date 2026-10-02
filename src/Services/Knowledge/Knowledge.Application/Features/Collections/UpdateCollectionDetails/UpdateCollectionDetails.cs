using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Collections.UpdateCollectionDetails;

/// <summary>
/// Changes the title and description of a collection. Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Input rules (<c>UpdateCollectionDetailsValidator</c>): <see cref="CollectionId"/> not empty; <see cref="Title"/> not blank, at most
/// <see cref="Knowledge.Domain.Collections.Collection.MaxTitleLength"/> characters after trimming; <see cref="Description"/> at most
/// <see cref="Knowledge.Domain.Collections.Collection.MaxDescriptionLength"/> characters after trimming.
/// </para>
/// <para>
/// Both fields are replaced (this is not a partial update): send the current description to keep it. Allowed for drafts and published
/// collections; the change is visible to readers immediately.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.NotFound"/> (<c>knowledge.collection.not_found</c>, 404).</description></item>
///   <item><description><see cref="Knowledge.Domain.Collections.CollectionErrors.Archived"/> (<c>knowledge.collection.archived</c>, 422).</description></item>
/// </list>
/// </remarks>
/// <param name="CollectionId">Identifier of the collection; must not be <see cref="Guid.Empty"/>.</param>
/// <param name="Title">New title; not blank, at most <see cref="Knowledge.Domain.Collections.Collection.MaxTitleLength"/> characters after trimming. Stored trimmed.</param>
/// <param name="Description">
/// New description, at most <see cref="Knowledge.Domain.Collections.Collection.MaxDescriptionLength"/> characters after trimming; <see langword="null"/>
/// or blank removes the description. Stored trimmed.
/// </param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record UpdateCollectionDetails(Guid CollectionId, string Title, string? Description) : ICommand;
