using SuperApp.Framework.Application.Persistence;
using SuperApp.Framework.Infrastructure.Caching;
using Knowledge.Application;
using Knowledge.Application.Features.Categories.CreateCategory;
using Knowledge.Application.Features.Categories.ListCategories;
using Knowledge.Application.Features.Materials.GetMaterial;
using Knowledge.Application.Features.Materials.UpdateMaterialDetails;
using Knowledge.Domain.Categories;
using Knowledge.Infrastructure.Caching;
using Knowledge.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SuperApp.Framework.Testing;

namespace Knowledge.IntegrationTests;

/// <summary>Cache entries are invalidated after the commit of a change, never before it and never after a rollback (ADR-0020).</summary>
[Trait("Category", "Integration")]
[Collection(PipelineCollection.Name)]
public sealed class CacheInvalidationTests(ServiceFixture fixture)
{
    [Fact]
    public async Task Category_list_is_invalidated_after_commit_and_read_returns_fresh_data()
    {
        var categoryId = await CreateCategoryAndCacheListAsync("Przed zmianą");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await RenameAsync(scope.ServiceProvider, categoryId, "Po zmianie");
            Assert.True((await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken)).IsSuccess);

            // Saved but not committed: the cached list must still be there, otherwise a concurrent read could cache the old list again.
            Assert.False(await CategoryListIsMissingAsync());

            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        var categories = ResultAssert.Success(await fixture.SendAsync(new ListCategories()));
        Assert.Contains(categories, category => category.Id == categoryId && category.Name == "Po zmianie");
    }

    [Fact]
    public async Task Rolled_back_change_does_not_invalidate_category_list()
    {
        var categoryId = await CreateCategoryAndCacheListAsync("Bez zmian");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await RenameAsync(scope.ServiceProvider, categoryId, "Wycofana zmiana");
            Assert.True((await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken)).IsSuccess);

            // Disposed without commit: the transaction rolls back.
        }

        Assert.False(await CategoryListIsMissingAsync());
        var categories = ResultAssert.Success(await fixture.SendAsync(new ListCategories()));
        Assert.Contains(categories, category => category.Id == categoryId && category.Name == "Bez zmian");
    }

    [Fact]
    public async Task Cached_material_view_is_fresh_after_update()
    {
        fixture.ActWith(KnowledgeScopes.CatalogWrite);
        var materialId = await fixture.CreatePublishedArticleAsync("Stary tytuł");

        fixture.ActWith(KnowledgeScopes.CatalogRead);
        Assert.Equal("Stary tytuł", ResultAssert.Success(await fixture.SendAsync(new GetMaterial(materialId))).Title);

        fixture.ActWith(KnowledgeScopes.CatalogWrite);
        Assert.True((await fixture.SendAsync(new UpdateMaterialDetails(materialId, "Nowy tytuł", null, null, null))).IsSuccess);

        fixture.ActWith(KnowledgeScopes.CatalogRead);
        Assert.Equal("Nowy tytuł", ResultAssert.Success(await fixture.SendAsync(new GetMaterial(materialId))).Title);
    }

    private async Task<Guid> CreateCategoryAndCacheListAsync(string name)
    {
        fixture.ActWith(KnowledgeScopes.CatalogWrite, KnowledgeScopes.CatalogRead);
        var created = ResultAssert.Success(await fixture.SendAsync(new CreateCategory(name, $"cache-{Guid.NewGuid():N}")));
        Assert.Contains(ResultAssert.Success(await fixture.SendAsync(new ListCategories())), category => category.Id == created);
        Assert.False(await CategoryListIsMissingAsync());
        return created;
    }

    private static async Task RenameAsync(IServiceProvider services, Guid categoryId, string name)
    {
        var category = await services.GetRequiredService<ICategoryRepository>()
            .GetAsync(CategoryId.FromTrusted(categoryId), TestContext.Current.CancellationToken);
        Assert.NotNull(category);
        Assert.True(category.Rename(name).IsSuccess);
    }

    // Reads the cached category list without touching the database: the factory only records that the entry was missing.
    private async Task<bool> CategoryListIsMissingAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<FailSafeCache>();
        var missing = false;
        await cache.GetOrCreateAsync<IReadOnlyList<CategoryDto>>(
            KnowledgeCache.CategoriesKey,
            _ =>
            {
                missing = true;
                return ValueTask.FromResult<IReadOnlyList<CategoryDto>>([]);
            },
            KnowledgeCache.Categories,
            [KnowledgeCache.CategoriesTag],
            TestContext.Current.CancellationToken);

        if (missing)
        {
            // Do not leave the placeholder behind for the following reads.
            await cache.RemoveByTagAsync(KnowledgeCache.CategoriesTag, TestContext.Current.CancellationToken);
        }

        return missing;
    }
}
