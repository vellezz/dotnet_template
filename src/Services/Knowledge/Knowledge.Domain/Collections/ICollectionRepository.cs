using Knowledge.Domain.Common;

namespace Knowledge.Domain.Collections;

/// <summary>
/// Write-side repository of the <see cref="Collection"/> aggregate: loads collections for modification and answers the status check
/// needed before adding a collection to favorites.
/// </summary>
/// <remarks>
/// Implemented in <c>Knowledge.Infrastructure</c> over the write <c>DbContext</c>. Changes are saved by the unit of work at the end of the
/// command; there is no save or update method. Reading collections for display goes through the read side (ADR-0026).
/// </remarks>
public interface ICollectionRepository
{
    /// <summary>Loads a collection with its items and categories, ready to be modified.</summary>
    /// <param name="id">Identifier of the collection.</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns>
    /// The tracked collection in any status, or <see langword="null"/> if it does not exist (handlers then return <see cref="CollectionErrors.NotFound"/>).
    /// </returns>
    Task<Collection?> GetAsync(CollectionId id, CancellationToken cancellationToken);

    /// <summary>
    /// Checks that a collection exists and has the <see cref="PublicationStatus.Published"/> status (used before adding it to favorites).
    /// </summary>
    /// <param name="id">Identifier of the collection.</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns><see langword="true"/> if the collection is published; <see langword="false"/> if it is missing, a draft or archived.</returns>
    Task<bool> IsPublishedAsync(CollectionId id, CancellationToken cancellationToken);

    /// <summary>Registers a newly created collection; it is inserted when the unit of work commits.</summary>
    /// <param name="collection">The new collection, as returned by <see cref="Collection.Create"/>.</param>
    void Add(Collection collection);
}
