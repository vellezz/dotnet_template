using Knowledge.Domain.Categories;
using Knowledge.Domain.Collections;
using Knowledge.Domain.Collections.Events;
using Knowledge.Domain.Materials;
using SuperApp.Framework.Testing;

namespace Knowledge.Domain.Tests;

public sealed class CollectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Category_limit_counts_distinct_categories()
    {
        var collection = ResultAssert.Success(Collection.Create("Kolekcja", null, Now));
        var category = CategoryId.New();
        var repeated = Enumerable.Repeat(category, Collection.MaxCategories + 5).ToList();

        Assert.True(collection.SetCategories(repeated, Now).IsSuccess);

        Assert.Single(collection.Categories);
    }

    [Fact]
    public void Category_limit_rejects_too_many_distinct_categories()
    {
        var collection = ResultAssert.Success(Collection.Create("Kolekcja", null, Now));
        var tooMany = Enumerable.Range(0, Collection.MaxCategories + 1).Select(_ => CategoryId.New()).ToList();

        Assert.Equal(CollectionErrors.TooManyCategories, collection.SetCategories(tooMany, Now).Error);
    }

    [Fact]
    public void Duplicates_are_reported_before_the_item_limit()
    {
        var collection = ResultAssert.Success(Collection.Create("Kolekcja", null, Now));
        var material = MaterialId.New();
        var repeated = Enumerable.Repeat(material, Collection.MaxItems + 1).ToList();

        Assert.Equal(CollectionErrors.DuplicateItems, collection.SetItems(repeated, Now).Error);
    }

    [Fact]
    public void Item_limit_rejects_too_many_distinct_materials()
    {
        var collection = ResultAssert.Success(Collection.Create("Kolekcja", null, Now));
        var tooMany = Enumerable.Range(0, Collection.MaxItems + 1).Select(_ => MaterialId.New()).ToList();

        Assert.Equal(CollectionErrors.TooManyItems, collection.SetItems(tooMany, Now).Error);
    }

    [Fact]
    public void Archived_event_carries_the_archiving_time_of_the_aggregate()
    {
        var collection = ResultAssert.Success(Collection.Create("Kolekcja", null, Now));
        var archivedAt = Now.AddHours(2);

        Assert.True(collection.Archive(archivedAt).IsSuccess);

        var archived = Assert.Single(collection.DomainEvents.OfType<CollectionArchived>());
        Assert.Equal(archivedAt, archived.ArchivedAt);
        Assert.Equal(collection.UpdatedAt, archived.ArchivedAt);
    }
}
