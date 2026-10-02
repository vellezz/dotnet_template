using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using Knowledge.Domain.Library.Favorites;

namespace Knowledge.Application.Features.Library.RemoveFavoritesOfItem;

/// <summary>
/// Removes the favorites of all users that point to an archived material or collection. Internal use case sent by the Worker, not
/// exposed over HTTP. Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Sent by <c>MaterialArchivedConsumer</c> and <c>CollectionArchivedConsumer</c> in <c>Knowledge.Worker</c> after they receive
/// <c>MaterialArchivedV1</c> or <c>CollectionArchivedV1</c> (eventual consistency between the catalog and the libraries). Worker requests
/// run as the system identity, which holds every scope.
/// </para>
/// <para>
/// This is a deliberate exception to "one transaction changes one aggregate" (ADR-0028): all <c>Favorite</c> aggregates of the item are
/// deleted with a single set-based statement, which is safe because deleting a favorite cannot break an invariant of any other favorite.
/// The operation is idempotent, so redelivered messages are harmless. The item's status is not checked here.
/// </para>
/// <para>Input rules (<c>RemoveFavoritesOfItemValidator</c>): <see cref="ItemType"/> is a defined value; <see cref="ItemId"/> not empty.</para>
/// <para>
/// Result: success without a value, also when there was nothing to delete. Possible errors: <c>auth.unauthenticated</c> (403),
/// <c>auth.missing_scope</c> (403), <c>validation.failed</c> (400). The consumers log a rejected command instead of retrying it.
/// </para>
/// </remarks>
/// <param name="ItemType">Kind of archived item: <c>Material</c> or <c>Collection</c> (<see cref="FavoriteItemType"/>).</param>
/// <param name="ItemId">Identifier of the archived material or collection; must not be <see cref="Guid.Empty"/>.</param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record RemoveFavoritesOfItem(FavoriteItemType ItemType, Guid ItemId) : ICommand;
