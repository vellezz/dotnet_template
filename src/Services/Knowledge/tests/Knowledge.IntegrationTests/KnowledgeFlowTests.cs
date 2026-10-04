using Knowledge.Application.Content.Blocks;
using Knowledge.Domain.Library.Favorites;
using Knowledge.IntegrationTests.Infrastructure;
using System.Text.Json;
using Knowledge.Application;
using Knowledge.Application.Content;
using Knowledge.Application.Features.Categories.CreateCategory;
using Knowledge.Application.Features.Library.AddFavorite;
using Knowledge.Application.Features.Library.ListMyCompletedMaterials;
using Knowledge.Application.Features.Library.ListMyFavorites;
using Knowledge.Application.Features.Library.MarkMaterialCompleted;
using Knowledge.Application.Features.Library.RemoveFavoritesOfItem;
using Knowledge.Application.Features.Materials.ArchiveMaterial;
using Knowledge.Application.Features.Materials.CreateMaterial;
using Knowledge.Application.Features.Materials.GetMaterial;
using Knowledge.Application.Features.Materials.ListMaterials;
using Knowledge.Application.Features.Materials.PublishMaterial;
using Knowledge.Application.Features.Materials.ReplaceMaterialContent;
using Knowledge.Application.Features.Materials.SetMaterialCategories;
using Knowledge.Contracts;
using Knowledge.Domain.Materials;
using Knowledge.Domain.Materials.Content;
using Knowledge.Infrastructure.Persistence.Write;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SuperApp.Framework.Testing;

namespace Knowledge.IntegrationTests;

/// <summary>End-to-end flows through the MediatR pipeline on MSSQL (Testcontainers); tests in this class run sequentially.</summary>
[Trait("Category", "Integration")]
[Collection(PipelineCollection.Name)]
public sealed class KnowledgeFlowTests(ServiceFixture fixture)
{
    private static readonly SpanDto[] Text = [new("Sen jest ważny", [TextMarks.Bold], "https://example.com/sen")];

    private static readonly IReadOnlyList<ContentBlockDto> Content =
    [
        new HeadingBlockDto(1, "Higiena snu"),
        new ParagraphBlockDto(Text),
        new ListBlockDto(ListStyle.Ordered, [new ListItemDto(Text, new ListBlockDto(ListStyle.Unordered, [new ListItemDto(Text)]))]),
        new CalloutBlockDto(CalloutVariant.Tip, [new ParagraphBlockDto(Text)], "Wskazówka"),
        new TableBlockDto(true, [new TableRowDto([new TableCellDto(Text), new TableCellDto(Text)])]),
        new ImageBlockDto("https://cdn.example.com/sen.png", "Sypialnia"),
        new CodeBlockDto("sleep 8h", "bash"),
    ];

    [Fact]
    public async Task Material_is_created_published_and_read_with_identical_content()
    {
        Act(KnowledgeScopes.CatalogWrite);
        var category = ResultAssert.Success(await fixture.SendAsync(new CreateCategory("Sen", $"sen-{Guid.NewGuid():N}")));
        var materialId = await CreatePublishedMaterialAsync();
        Assert.True((await fixture.SendAsync(new SetMaterialCategories(materialId, [category]))).IsSuccess);

        Act(KnowledgeScopes.CatalogRead);
        var material = ResultAssert.Success(await fixture.SendAsync(new GetMaterial(materialId)));
        var list = ResultAssert.Success(await fixture.SendAsync(new ListMaterials(category, MaterialType.Article)));

        Assert.Equal(JsonSerializer.Serialize(Content), JsonSerializer.Serialize(material.Content));
        Assert.True(material.ReadingTimeMinutes >= 1);
        Assert.Contains(list.Items, item => item.Id == materialId);
        Assert.Contains(fixture.Publisher.Published, message => message is MaterialPublishedV1 published && published.MaterialId == materialId);
    }

    [Fact]
    public async Task Draft_is_visible_only_to_editor()
    {
        Act(KnowledgeScopes.CatalogWrite, KnowledgeScopes.CatalogRead);
        var draft = ResultAssert.Success(await fixture.SendAsync(new CreateMaterial(MaterialType.Article, "Szkic", null, null, null)));

        var asEditor = await fixture.SendAsync(new GetMaterial(draft));
        Act(KnowledgeScopes.CatalogRead);
        var asReader = await fixture.SendAsync(new GetMaterial(draft));

        Assert.True(asEditor.IsSuccess);
        Assert.Equal(MaterialErrors.NotFound, asReader.Error);
    }

    [Fact]
    public async Task Library_tracks_favorites_and_completions_and_archiving_cleans_favorites()
    {
        Act(KnowledgeScopes.CatalogWrite);
        var materialId = await CreatePublishedMaterialAsync();

        Act(KnowledgeScopes.LibraryWrite, KnowledgeScopes.LibraryRead);
        Assert.True((await fixture.SendAsync(new AddFavorite(FavoriteItemType.Material, materialId))).IsSuccess);
        Assert.True((await fixture.SendAsync(new AddFavorite(FavoriteItemType.Material, materialId))).IsSuccess);
        Assert.True((await fixture.SendAsync(new MarkMaterialCompleted(materialId))).IsSuccess);

        var favorites = ResultAssert.Success(await fixture.SendAsync(new ListMyFavorites()));
        var completions = ResultAssert.Success(await fixture.SendAsync(new ListMyCompletedMaterials()));
        Assert.Single(favorites.Items, item => item.ItemId == materialId);
        Assert.Single(completions.Items, item => item.MaterialId == materialId);

        Act(KnowledgeScopes.CatalogWrite);
        Assert.True((await fixture.SendAsync(new ArchiveMaterial(materialId))).IsSuccess);
        Assert.Contains(fixture.Publisher.Published, message => message is MaterialArchivedV1 archived && archived.MaterialId == materialId);

        // The MaterialArchivedV1 consumer in the Worker sends this command (ADR-0028).
        Assert.True((await fixture.SendAsync(new RemoveFavoritesOfItem(FavoriteItemType.Material, materialId))).IsSuccess);
        Act(KnowledgeScopes.LibraryRead);
        Assert.DoesNotContain(ResultAssert.Success(await fixture.SendAsync(new ListMyFavorites())).Items, item => item.ItemId == materialId);
    }

    [Fact]
    public async Task Database_rejects_invalid_block_even_bypassing_domain()
    {
        Act(KnowledgeScopes.CatalogWrite);
        var materialId = ResultAssert.Success(await fixture.SendAsync(new CreateMaterial(MaterialType.Article, "Materiał", null, null, null)));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeWriteDbContext>();

        var exception = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO [knowledge].[ContentBlocks] ([Id], [MaterialId], [Position], [Type], [Level]) VALUES ({0}, {1}, 0, 'Heading', 9)",
            [Guid.NewGuid(), materialId],
            TestContext.Current.CancellationToken));

        Assert.Contains("CK_ContentBlocks_Heading", exception.Message, StringComparison.Ordinal);
    }

    private async Task<Guid> CreatePublishedMaterialAsync()
    {
        var created = ResultAssert.Success(await fixture.SendAsync(new CreateMaterial(MaterialType.Article, "Jak spać lepiej", "Poradnik", null, null)));
        Assert.True((await fixture.SendAsync(new ReplaceMaterialContent(created, Content))).IsSuccess);
        Assert.True((await fixture.SendAsync(new PublishMaterial(created))).IsSuccess);
        return created;
    }

    private void Act(params string[] scopes)
    {
        fixture.CurrentUser.Scopes.Clear();
        fixture.CurrentUser.Scopes.UnionWith(scopes);
    }
}
