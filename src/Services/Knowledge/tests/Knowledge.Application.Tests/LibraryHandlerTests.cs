using SuperApp.Framework.Domain.Results;
using Knowledge.Application.Tests.Fakes;
using Knowledge.Domain.Library.Favorites;
using Knowledge.Application.Features.Library.AddFavorite;
using Knowledge.Domain.Library;
using Knowledge.Domain.Materials;
using Knowledge.Domain.Materials.Content;
using SuperApp.Framework.Testing;

namespace Knowledge.Application.Tests;

public sealed class LibraryHandlerTests
{
    private readonly FakeClock _clock = new();
    private readonly FakeMaterialRepository _materials = new();
    private readonly FakeFavoriteRepository _favorites = new();

    [Fact]
    public async Task Adding_unpublished_material_to_favorites_is_rejected()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Szkic", null, null, null, _clock.UtcNow));
        _materials.Add(material);

        var result = await Handler("user-1").Handle(new AddFavorite(FavoriteItemType.Material, material.Id.Value), TestContext.Current.CancellationToken);

        Assert.Equal(LibraryErrors.ItemNotAvailable, result.Error);
        Assert.Empty(_favorites.Items);
    }

    [Fact]
    public async Task Adding_favorite_is_idempotent()
    {
        var material = PublishedMaterial();
        var command = new AddFavorite(FavoriteItemType.Material, material.Id.Value);

        Assert.True((await Handler("user-1").Handle(command, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await Handler("user-1").Handle(command, TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Single(_favorites.Items);
    }

    [Fact]
    public async Task Anonymous_user_cannot_add_favorites()
    {
        var material = PublishedMaterial();

        var result = await Handler(null).Handle(new AddFavorite(FavoriteItemType.Material, material.Id.Value), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.Forbidden, ResultAssert.Failure(result).Type);
    }

    private AddFavoriteHandler Handler(string? subject) =>
        new(_favorites, _materials, new FakeCollectionRepository(), new FakeCurrentUser(subject), _clock);

    private Material PublishedMaterial()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Artykuł", null, null, null, _clock.UtcNow));
        Assert.True(material.ReplaceContent([new BlockSpec(BlockType.Paragraph, Text: [new SpanSpec("Treść")])], _clock.UtcNow).IsSuccess);
        Assert.True(material.Publish(_clock.UtcNow).IsSuccess);
        _materials.Add(material);
        return material;
    }
}
