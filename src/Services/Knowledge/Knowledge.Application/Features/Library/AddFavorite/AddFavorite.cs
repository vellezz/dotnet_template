using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using Knowledge.Domain.Library.Favorites;
using Knowledge.Domain.Library;

namespace Knowledge.Application.Features.Library.AddFavorite;

/// <summary>
/// Adds a published material or collection to the favorites of the calling user. Requires scope <see cref="KnowledgeScopes.LibraryWrite"/>.
/// </summary>
/// <remarks>
/// <para>Input rules (<c>AddFavoriteValidator</c>): <see cref="ItemType"/> is a defined value; <see cref="ItemId"/> not empty.</para>
/// <para>
/// The handler takes the user from the token (<c>sub</c>), checks that the item exists and is published, and adds a <c>Favorite</c>
/// unless the user already has one for the item. The operation is idempotent: adding an existing favorite succeeds without a change
/// (the original <c>AddedAt</c> is kept). When the item is archived later, the Worker removes its favorites automatically.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403): no authenticated user or no <c>sub</c> claim; <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
///   <item><description><c>knowledge.user.invalid_id</c> (400): the <c>sub</c> claim is longer than
///   <see cref="Knowledge.Domain.Common.UserId.MaxLength"/> characters.</description></item>
///   <item><description><see cref="LibraryErrors.ItemNotAvailable"/> (<c>knowledge.library.item_not_available</c>, 404): the item does not
///   exist or is not published.</description></item>
///   <item><description><see cref="LibraryErrors.FavoriteAddedConcurrently"/> (<c>knowledge.library.favorite_added_concurrently</c>, 409):
///   only as the outcome of a race, when a concurrent request of the same user added the same favorite at the same moment. The favorite
///   exists afterwards; a retry succeeds.</description></item>
/// </list>
/// </remarks>
/// <param name="ItemType">Kind of item: <c>Material</c> or <c>Collection</c> (<see cref="FavoriteItemType"/>, serialized as a string).</param>
/// <param name="ItemId">Identifier of the material or collection, depending on <see cref="ItemType"/>; must not be <see cref="Guid.Empty"/>.</param>
[RequiresScope(KnowledgeScopes.LibraryWrite)]
public sealed record AddFavorite(FavoriteItemType ItemType, Guid ItemId) : ICommand;
