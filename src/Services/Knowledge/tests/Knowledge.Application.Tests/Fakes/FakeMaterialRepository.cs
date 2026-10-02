using Knowledge.Domain.Common;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Tests.Fakes;

internal sealed class FakeMaterialRepository : IMaterialRepository
{
    public List<Material> Items { get; } = [];

    public Task<Material?> GetAsync(MaterialId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(material => material.Id == id));

    public Task<bool> AllExistAsync(IReadOnlyCollection<MaterialId> ids, CancellationToken cancellationToken) =>
        Task.FromResult(ids.All(id => Items.Any(material => material.Id == id)));

    public Task<bool> IsPublishedAsync(MaterialId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(material => material.Id == id && material.Status == PublicationStatus.Published));

    public void Add(Material material) => Items.Add(material);
}
