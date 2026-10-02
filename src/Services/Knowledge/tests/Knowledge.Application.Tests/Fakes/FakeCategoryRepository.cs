using Knowledge.Domain.Categories;

namespace Knowledge.Application.Tests.Fakes;

internal sealed class FakeCategoryRepository : ICategoryRepository
{
    public List<Category> Items { get; } = [];

    public Task<Category?> GetAsync(CategoryId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(category => category.Id == id));

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(category => category.Slug == slug));

    public Task<bool> AllExistAsync(IReadOnlyCollection<CategoryId> ids, CancellationToken cancellationToken) =>
        Task.FromResult(ids.All(id => Items.Any(category => category.Id == id)));

    public void Add(Category category) => Items.Add(category);
}
