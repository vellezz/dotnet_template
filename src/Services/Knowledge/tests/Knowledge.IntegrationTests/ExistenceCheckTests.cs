using Knowledge.Application;
using Knowledge.Application.Features.Categories.CreateCategory;
using Knowledge.Application.Features.Collections.CreateCollection;
using Knowledge.Application.Features.Collections.SetCollectionItems;
using Knowledge.Application.Features.Materials.CreateMaterial;
using Knowledge.Domain.Categories;
using Knowledge.Domain.Collections;
using Knowledge.Domain.Materials;
using Knowledge.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SuperApp.Framework.Testing;

namespace Knowledge.IntegrationTests;

/// <summary>The existence checks of the repositories ignore duplicate identifiers instead of failing on them.</summary>
[Collection(PipelineCollection.Name)]
public sealed class ExistenceCheckTests(ServiceFixture fixture)
{
    [Fact]
    public async Task Categories_exist_even_when_an_identifier_is_repeated()
    {
        fixture.ActWith(KnowledgeScopes.CatalogWrite);
        var category = CategoryId.FromTrusted(ResultAssert.Success(await fixture.SendAsync(new CreateCategory("Istnieje", $"exists-{Guid.NewGuid():N}"))));

        await using var scope = fixture.Services.CreateAsyncScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();

        Assert.True(await categories.AllExistAsync([category, category, category], TestContext.Current.CancellationToken));
        Assert.False(await categories.AllExistAsync([category, category, CategoryId.New()], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Materials_exist_even_when_an_identifier_is_repeated()
    {
        fixture.ActWith(KnowledgeScopes.CatalogWrite);
        var material = MaterialId.FromTrusted(ResultAssert.Success(await fixture.SendAsync(new CreateMaterial(MaterialType.Article, "Istnieje", null, null, null))));

        await using var scope = fixture.Services.CreateAsyncScope();
        var materials = scope.ServiceProvider.GetRequiredService<IMaterialRepository>();

        Assert.True(await materials.AllExistAsync([material, material], TestContext.Current.CancellationToken));
        Assert.False(await materials.AllExistAsync([material, MaterialId.New()], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repeated_material_in_collection_is_reported_as_duplicate_not_as_unknown()
    {
        fixture.ActWith(KnowledgeScopes.CatalogWrite);
        var material = ResultAssert.Success(await fixture.SendAsync(new CreateMaterial(MaterialType.Article, "Powtórzony", null, null, null)));
        var collection = ResultAssert.Success(await fixture.SendAsync(new CreateCollection("Kolekcja", null)));

        var result = await fixture.SendAsync(new SetCollectionItems(collection, [material, material]));

        Assert.Equal(CollectionErrors.DuplicateItems, result.Error);
    }
}
