using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using Knowledge.Domain.Library.Favorites;

namespace Knowledge.Application.Features.Library.RemoveFavorite;

/// <summary>
/// Removes a material or collection from the calling user's favorites. Requires scope <see cref="KnowledgeScopes.LibraryWrite"/>.
/// </summary>
/// <remarks>
/// <para>Input rules (<c>RemoveFavoriteValidator</c>): <see cref="ItemType"/> is a defined value; <see cref="ItemId"/> not empty.</para>
/// <para>
/// The handler takes the user from the token (<c>sub</c>) and deletes that user's favorite for the item. The operation is idempotent:
/// when there is no such favorite (or the item does not exist at all) it succeeds without a change. The item's status is not checked,
/// so favorites of archived items can be removed as well.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403): no authenticated user or no <c>sub</c> claim; <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
///   <item><description><c>knowledge.user.invalid_id</c> (400): the <c>sub</c> claim is longer than
///   <see cref="Knowledge.Domain.Common.UserId.MaxLength"/> characters.</description></item>
/// </list>
/// </remarks>
/// <param name="ItemType">Kind of item: <c>Material</c> or <c>Collection</c> (<see cref="FavoriteItemType"/>, serialized as a string).</param>
/// <param name="ItemId">Identifier of the material or collection, depending on <see cref="ItemType"/>; must not be <see cref="Guid.Empty"/>.</param>
[RequiresScope(KnowledgeScopes.LibraryWrite)]
public sealed record RemoveFavorite(FavoriteItemType ItemType, Guid ItemId) : ICommand;
