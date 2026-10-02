using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Collections;

namespace Knowledge.Application.Features.Collections.PublishCollection;

/// <summary>
/// Handles <see cref="PublishCollection"/>: loads the collection and delegates to <see cref="Collection.Publish"/> with the current time.
/// </summary>
/// <remarks>An identifier that cannot be converted to <see cref="CollectionId"/> is reported as <see cref="CollectionErrors.NotFound"/>.</remarks>
internal sealed class PublishCollectionHandler(ICollectionRepository collections, IClock clock) : ICommandHandler<PublishCollection>
{
    /// <inheritdoc />
    public async Task<Result> Handle(PublishCollection command, CancellationToken cancellationToken)
    {
        if (!CollectionId.Create(command.CollectionId).TryGetValue(out var collectionId, out _))
        {
            return CollectionErrors.NotFound;
        }

        var collection = await collections.GetAsync(collectionId, cancellationToken);
        return collection is null ? CollectionErrors.NotFound : collection.Publish(clock.UtcNow);
    }
}
