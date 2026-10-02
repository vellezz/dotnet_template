using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Collections;

namespace Knowledge.Application.Features.Collections.CreateCollection;

/// <summary>
/// Handles <see cref="CreateCollection"/>: creates a draft <see cref="Collection"/> with the current time and adds it to the repository.
/// </summary>
/// <remarks>Saving happens in the transaction behavior after the handler returns success.</remarks>
internal sealed class CreateCollectionHandler(ICollectionRepository collections, IClock clock) : ICommandHandler<CreateCollection, Result<Guid>>
{
    /// <inheritdoc />
    public Task<Result<Guid>> Handle(CreateCollection command, CancellationToken cancellationToken)
    {
        if (!Collection.Create(command.Title, command.Description, clock.UtcNow).TryGetValue(out var collection, out var error))
        {
            return Task.FromResult<Result<Guid>>(error);
        }

        collections.Add(collection);
        return Task.FromResult<Result<Guid>>(collection.Id.Value);
    }
}
