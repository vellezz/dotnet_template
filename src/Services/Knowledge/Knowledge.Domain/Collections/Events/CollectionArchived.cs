using SuperApp.Framework.Domain.Events;
using Knowledge.Domain.Common;

namespace Knowledge.Domain.Collections.Events;

/// <summary>
/// Domain event: a collection has been archived and will never change again. Raised by <see cref="Collection.Archive"/> on the transition
/// to <see cref="PublicationStatus.Archived"/>.
/// </summary>
/// <remarks>
/// <c>CollectionArchivedTranslator</c> turns it into the integration event <c>CollectionArchivedV1</c> with <see cref="ArchivedAt"/>, published through the outbox in the
/// same transaction (ADR-0027); <c>Knowledge.Worker</c> consumes it and removes the collection from all users' favorites.
/// </remarks>
/// <param name="CollectionId">Identifier of the archived collection.</param>
/// <param name="ArchivedAt">
/// Moment of archiving: the <c>now</c> passed to <see cref="Collection.Archive"/>, which is also stored as <see cref="Collection.UpdatedAt"/>.
/// </param>
public sealed record CollectionArchived(CollectionId CollectionId, DateTimeOffset ArchivedAt) : IDomainEvent;
