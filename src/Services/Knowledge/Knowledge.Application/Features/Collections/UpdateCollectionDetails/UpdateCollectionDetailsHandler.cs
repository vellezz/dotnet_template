using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Collections;

namespace Knowledge.Application.Features.Collections.UpdateCollectionDetails;

/// <summary>
/// Handles <see cref="UpdateCollectionDetails"/>: loads the collection and delegates to <see cref="Collection.UpdateDetails"/>.
/// </summary>
/// <remarks>An identifier that cannot be converted to <see cref="CollectionId"/> is reported as <see cref="CollectionErrors.NotFound"/>.</remarks>
internal sealed class UpdateCollectionDetailsHandler(ICollectionRepository collections, IClock clock) : ICommandHandler<UpdateCollectionDetails>
{
    /// <inheritdoc />
    public async Task<Result> Handle(UpdateCollectionDetails command, CancellationToken cancellationToken)
    {
        if (!CollectionId.Create(command.CollectionId).TryGetValue(out var collectionId, out _))
        {
            return CollectionErrors.NotFound;
        }

        var collection = await collections.GetAsync(collectionId, cancellationToken);
        return collection is null ? CollectionErrors.NotFound : collection.UpdateDetails(command.Title, command.Description, clock.UtcNow);
    }
}
