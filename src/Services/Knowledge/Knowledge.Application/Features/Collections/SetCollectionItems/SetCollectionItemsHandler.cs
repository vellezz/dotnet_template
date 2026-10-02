using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Collections;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Collections.SetCollectionItems;

/// <summary>
/// Handles <see cref="SetCollectionItems"/>: loads the collection, verifies that all referenced materials exist and delegates the
/// replacement to <see cref="Collection.SetItems"/>.
/// </summary>
/// <remarks>
/// Duplicates are kept in the list passed to the aggregate so that it can report <see cref="CollectionErrors.DuplicateItems"/>; only the
/// existence check uses distinct identifiers. Materials are another aggregate, so this is a point-in-time check, not a foreign key.
/// </remarks>
internal sealed class SetCollectionItemsHandler(ICollectionRepository collections, IMaterialRepository materials, IClock clock)
    : ICommandHandler<SetCollectionItems>
{
    /// <inheritdoc />
    public async Task<Result> Handle(SetCollectionItems command, CancellationToken cancellationToken)
    {
        if (!CollectionId.Create(command.CollectionId).TryGetValue(out var collectionId, out _))
        {
            return CollectionErrors.NotFound;
        }

        var collection = await collections.GetAsync(collectionId, cancellationToken);
        if (collection is null)
        {
            return CollectionErrors.NotFound;
        }

        var materialIds = new List<MaterialId>();
        foreach (var rawId in command.MaterialIds)
        {
            if (!MaterialId.Create(rawId).TryGetValue(out var materialId, out _))
            {
                return CollectionErrors.UnknownMaterials;
            }

            materialIds.Add(materialId);
        }

        if (!await materials.AllExistAsync(materialIds.Distinct().ToList(), cancellationToken))
        {
            return CollectionErrors.UnknownMaterials;
        }

        return collection.SetItems(materialIds, clock.UtcNow);
    }
}
