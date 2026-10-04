using Knowledge.Application;
using Knowledge.Application.Features.Categories.CreateCategory;
using Knowledge.Application.Features.Collections.CreateCollection;
using Knowledge.Application.Features.Collections.GetCollection;
using Knowledge.Application.Features.Collections.ListCollections;
using Knowledge.Application.Features.Collections.PublishCollection;
using Knowledge.Application.Features.Collections.SetCollectionCategories;
using Knowledge.Application.Features.Collections.SetCollectionItems;
using Knowledge.Application.Features.Library.ListMyCompletedMaterials;
using Knowledge.Application.Features.Library.MarkMaterialCompleted;
using Knowledge.Application.Features.Materials.ArchiveMaterial;
using Knowledge.Application.Features.Materials.CreateMaterial;
using Knowledge.Domain.Materials;
using Knowledge.IntegrationTests.Infrastructure;
using SuperApp.Framework.Testing;

namespace Knowledge.IntegrationTests;

/// <summary>Readers (without <c>knowledge.catalog.write</c>) never see or count draft and archived materials.</summary>
[Trait("Category", "Integration")]
[Collection(PipelineCollection.Name)]
public sealed class ReaderVisibilityTests(ServiceFixture fixture)
{
    [Fact]
    public async Task Collection_lists_and_counts_only_published_materials_for_readers()
    {
        fixture.ActWith(KnowledgeScopes.CatalogWrite);
        var published = await fixture.CreatePublishedArticleAsync("Opublikowany");
        var archived = await fixture.CreatePublishedArticleAsync("Zarchiwizowany");
        var draft = ResultAssert.Success(await fixture.SendAsync(new CreateMaterial(MaterialType.Article, "Szkic", null, null, null)));
        var category = ResultAssert.Success(await fixture.SendAsync(new CreateCategory("Widoczność", $"visibility-{Guid.NewGuid():N}")));
        var collection = ResultAssert.Success(await fixture.SendAsync(new CreateCollection("Kurs", null)));
        Assert.True((await fixture.SendAsync(new SetCollectionItems(collection, [published, archived, draft]))).IsSuccess);
        Assert.True((await fixture.SendAsync(new SetCollectionCategories(collection, [category]))).IsSuccess);
        Assert.True((await fixture.SendAsync(new PublishCollection(collection))).IsSuccess);
        Assert.True((await fixture.SendAsync(new ArchiveMaterial(archived))).IsSuccess);

        fixture.ActWith(KnowledgeScopes.CatalogRead);
        var readerList = ResultAssert.Success(await fixture.SendAsync(new ListCollections(category)));
        var readerDetails = ResultAssert.Success(await fixture.SendAsync(new GetCollection(collection)));

        fixture.ActWith(KnowledgeScopes.CatalogRead, KnowledgeScopes.CatalogWrite);
        var editorList = ResultAssert.Success(await fixture.SendAsync(new ListCollections(category)));
        var editorDetails = ResultAssert.Success(await fixture.SendAsync(new GetCollection(collection)));

        Assert.Equal(1, Assert.Single(readerList.Items).MaterialCount);
        Assert.Equal([published], readerDetails.Materials.Select(material => material.Id));
        Assert.Equal(3, Assert.Single(editorList.Items).MaterialCount);
        Assert.Equal([published, archived, draft], editorDetails.Materials.Select(material => material.Id));
    }

    [Fact]
    public async Task Completed_materials_list_contains_only_published_materials()
    {
        fixture.ActWith(KnowledgeScopes.CatalogWrite);
        var published = await fixture.CreatePublishedArticleAsync("Ukończony");
        var archived = await fixture.CreatePublishedArticleAsync("Ukończony i zarchiwizowany");

        var subject = fixture.CurrentUser.Subject;
        fixture.CurrentUser.Subject = $"reader-{Guid.NewGuid():N}";
        try
        {
            fixture.ActWith(KnowledgeScopes.LibraryWrite, KnowledgeScopes.LibraryRead);
            Assert.True((await fixture.SendAsync(new MarkMaterialCompleted(published))).IsSuccess);
            Assert.True((await fixture.SendAsync(new MarkMaterialCompleted(archived))).IsSuccess);

            fixture.ActWith(KnowledgeScopes.CatalogWrite);
            Assert.True((await fixture.SendAsync(new ArchiveMaterial(archived))).IsSuccess);

            fixture.ActWith(KnowledgeScopes.LibraryRead);
            var completed = ResultAssert.Success(await fixture.SendAsync(new ListMyCompletedMaterials()));

            Assert.Equal(published, Assert.Single(completed.Items).MaterialId);
            Assert.Equal(1, completed.TotalCount);
        }
        finally
        {
            fixture.CurrentUser.Subject = subject;
        }
    }
}
