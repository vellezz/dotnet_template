using Knowledge.Domain.Materials.Events;
using Knowledge.Domain.Categories;
using Knowledge.Domain.Collections;
using Knowledge.Domain.Common;
using Knowledge.Domain.Materials;
using Knowledge.Domain.Materials.Content;
using SuperApp.Framework.Testing;

namespace Knowledge.Domain.Tests;

public sealed class MaterialTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly BlockSpec[] Content = [new(BlockType.Paragraph, Text: [new SpanSpec("Treść")])];

    [Fact]
    public void Article_cannot_have_main_media()
    {
        var result = Material.Create(MaterialType.Article, "Tytuł", null, "https://cdn.example.com/a.mp4", null, Now);

        Assert.Equal(MaterialErrors.MediaNotAllowedForArticle, result.Error);
    }

    [Fact]
    public void Publishing_requires_content()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Tytuł", null, null, null, Now));

        Assert.Equal(MaterialErrors.ContentRequired, material.Publish(Now).Error);
    }

    [Fact]
    public void Publishing_video_requires_main_media()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Video, "Tytuł", null, null, null, Now));
        Assert.True(material.ReplaceContent(Content, Now).IsSuccess);

        Assert.Equal(MaterialErrors.MainMediaRequired, material.Publish(Now).Error);
    }

    [Fact]
    public void Publishing_raises_domain_event_once()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Tytuł", null, null, null, Now));
        Assert.True(material.ReplaceContent(Content, Now).IsSuccess);
        material.ClearDomainEvents();

        Assert.True(material.Publish(Now).IsSuccess);
        Assert.True(material.Publish(Now).IsSuccess);

        Assert.Single(material.DomainEvents.OfType<MaterialPublished>());
        Assert.Equal(PublicationStatus.Published, material.Status);
    }

    [Fact]
    public void Published_material_cannot_lose_its_content()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Tytuł", null, null, null, Now));
        Assert.True(material.ReplaceContent(Content, Now).IsSuccess);
        Assert.True(material.Publish(Now).IsSuccess);

        Assert.Equal(MaterialErrors.ContentRequired, material.ReplaceContent([], Now).Error);
    }

    [Fact]
    public void Archived_material_cannot_be_changed_and_raises_event()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Tytuł", null, null, null, Now));

        Assert.True(material.Archive(Now).IsSuccess);

        Assert.Contains(material.DomainEvents, domainEvent => domainEvent is MaterialArchived);
        Assert.Equal(MaterialErrors.Archived, material.UpdateDetails("Nowy", null, null, null, Now).Error);
    }

    [Fact]
    public void Published_event_carries_the_publication_time_of_the_aggregate()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Tytuł", null, null, null, Now));
        Assert.True(material.ReplaceContent(Content, Now).IsSuccess);
        var publishedAt = Now.AddMinutes(5);

        Assert.True(material.Publish(publishedAt).IsSuccess);

        var published = Assert.Single(material.DomainEvents.OfType<MaterialPublished>());
        Assert.Equal(publishedAt, published.PublishedAt);
        Assert.Equal(material.PublishedAt, published.PublishedAt);
    }

    [Fact]
    public void Archived_event_carries_the_archiving_time_of_the_aggregate()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Tytuł", null, null, null, Now));
        var archivedAt = Now.AddHours(1);

        Assert.True(material.Archive(archivedAt).IsSuccess);

        var archived = Assert.Single(material.DomainEvents.OfType<MaterialArchived>());
        Assert.Equal(archivedAt, archived.ArchivedAt);
        Assert.Equal(material.UpdatedAt, archived.ArchivedAt);
    }

    [Fact]
    public void Category_limit_counts_distinct_categories()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Tytuł", null, null, null, Now));
        var category = CategoryId.New();
        var repeated = Enumerable.Repeat(category, Material.MaxCategories + 5).ToList();

        Assert.True(material.SetCategories(repeated, Now).IsSuccess);

        Assert.Equal(category, Assert.Single(material.Categories).CategoryId);
    }

    [Fact]
    public void Category_limit_rejects_too_many_distinct_categories()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Tytuł", null, null, null, Now));
        var tooMany = Enumerable.Range(0, Material.MaxCategories + 1).Select(_ => CategoryId.New()).ToList();

        Assert.Equal(MaterialErrors.TooManyCategories, material.SetCategories(tooMany, Now).Error);
    }

    [Theory]
    [InlineData("zdrowy-sen")]
    [InlineData("a1")]
    public void Category_accepts_valid_slug(string slug) =>
        Assert.True(Category.Create("Sen", slug).IsSuccess);

    [Theory]
    [InlineData("Zdrowy sen")]
    [InlineData("-sen")]
    [InlineData("sen--zdrowy")]
    public void Category_rejects_invalid_slug(string slug) =>
        Assert.Equal(CategoryErrors.InvalidSlug, Category.Create("Sen", slug).Error);

    [Fact]
    public void Collection_rejects_duplicate_materials()
    {
        var collection = ResultAssert.Success(Collection.Create("Kolekcja", null, Now));
        var material = MaterialId.New();

        Assert.Equal(CollectionErrors.DuplicateItems, collection.SetItems([material, material], Now).Error);
    }

    [Fact]
    public void Collection_keeps_item_order()
    {
        var collection = ResultAssert.Success(Collection.Create("Kolekcja", null, Now));
        var first = MaterialId.New();
        var second = MaterialId.New();

        Assert.True(collection.SetItems([second, first], Now).IsSuccess);

        Assert.Equal([second, first], collection.Items.OrderBy(item => item.Position).Select(item => item.MaterialId));
    }
}
