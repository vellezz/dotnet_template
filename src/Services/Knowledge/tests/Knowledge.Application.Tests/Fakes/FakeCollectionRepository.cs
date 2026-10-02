using Knowledge.Domain.Collections;
using Knowledge.Domain.Common;

namespace Knowledge.Application.Tests.Fakes;

internal sealed class FakeCollectionRepository : ICollectionRepository
{
    public List<Collection> Items { get; } = [];

    public Task<Collection?> GetAsync(CollectionId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(collection => collection.Id == id));

    public Task<bool> IsPublishedAsync(CollectionId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(collection => collection.Id == id && collection.Status == PublicationStatus.Published));

    public void Add(Collection collection) => Items.Add(collection);
}
