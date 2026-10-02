using SuperApp.Framework.Application.Persistence;
using Knowledge.Domain.Categories;
using Knowledge.Domain.Common;
using Knowledge.Domain.Library;
using Knowledge.Domain.Library.Completions;
using Knowledge.Domain.Library.Favorites;
using Knowledge.Domain.Materials;
using Knowledge.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SuperApp.Framework.Testing;

namespace Knowledge.IntegrationTests;

/// <summary>
/// A race that slips past a handler's uniqueness check is reported by the unit of work as an error of the Knowledge context,
/// not as an exception. Two conflicting entities added in one scope reproduce the race deterministically.
/// </summary>
[Collection(PipelineCollection.Name)]
public sealed class UniqueConstraintTests(ServiceFixture fixture)
{
    [Fact]
    public async Task Duplicate_category_slug_is_reported_as_slug_taken()
    {
        var slug = $"race-{Guid.NewGuid():N}";

        var result = await SaveAsync(services =>
        {
            var categories = services.GetRequiredService<ICategoryRepository>();
            categories.Add(ResultAssert.Success(Category.Create("Pierwsza", slug)));
            categories.Add(ResultAssert.Success(Category.Create("Druga", slug)));
        });

        Assert.Equal(CategoryErrors.SlugTaken, result.Error);
    }

    [Fact]
    public async Task Duplicate_favorite_is_reported_as_added_concurrently()
    {
        var userId = ResultAssert.Success(UserId.Create($"race-{Guid.NewGuid():N}"));
        var itemId = Guid.NewGuid();

        var result = await SaveAsync(services =>
        {
            var favorites = services.GetRequiredService<IFavoriteRepository>();
            favorites.Add(Favorite.Add(userId, FavoriteItemType.Material, itemId, DateTimeOffset.UtcNow));
            favorites.Add(Favorite.Add(userId, FavoriteItemType.Material, itemId, DateTimeOffset.UtcNow));
        });

        Assert.Equal(LibraryErrors.FavoriteAddedConcurrently, result.Error);
    }

    [Fact]
    public async Task Duplicate_completion_is_reported_as_recorded_concurrently()
    {
        var userId = ResultAssert.Success(UserId.Create($"race-{Guid.NewGuid():N}"));
        var materialId = MaterialId.New();

        var result = await SaveAsync(services =>
        {
            var completions = services.GetRequiredService<IMaterialCompletionRepository>();
            completions.Add(MaterialCompletion.Complete(userId, materialId, DateTimeOffset.UtcNow));
            completions.Add(MaterialCompletion.Complete(userId, materialId, DateTimeOffset.UtcNow));
        });

        Assert.Equal(LibraryErrors.CompletionRecordedConcurrently, result.Error);
    }

    private async Task<SuperApp.Framework.Domain.Results.Result> SaveAsync(Action<IServiceProvider> addConflictingEntities)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        addConflictingEntities(scope.ServiceProvider);
        return await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
