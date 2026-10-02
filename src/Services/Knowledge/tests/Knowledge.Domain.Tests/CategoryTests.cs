using Knowledge.Domain.Categories;
using Knowledge.Domain.Categories.Events;
using SuperApp.Framework.Testing;

namespace Knowledge.Domain.Tests;

public sealed class CategoryTests
{
    [Fact]
    public void Renaming_to_the_current_name_changes_nothing_and_raises_no_event()
    {
        var category = ResultAssert.Success(Category.Create("Sen", "sen"));
        category.ClearDomainEvents();

        Assert.True(category.Rename("Sen").IsSuccess);
        Assert.True(category.Rename("  Sen  ").IsSuccess);

        Assert.Equal("Sen", category.Name);
        Assert.Empty(category.DomainEvents);
    }

    [Fact]
    public void Renaming_to_a_different_name_raises_event()
    {
        var category = ResultAssert.Success(Category.Create("Sen", "sen"));
        category.ClearDomainEvents();

        Assert.True(category.Rename("Zdrowy sen").IsSuccess);

        Assert.Equal("Zdrowy sen", category.Name);
        Assert.Equal(category.Id, Assert.Single(category.DomainEvents.OfType<CategoryChanged>()).CategoryId);
    }

    [Fact]
    public void Changing_only_letter_case_is_a_rename()
    {
        var category = ResultAssert.Success(Category.Create("sen", "sen"));
        category.ClearDomainEvents();

        Assert.True(category.Rename("Sen").IsSuccess);

        Assert.Equal("Sen", category.Name);
        Assert.Single(category.DomainEvents.OfType<CategoryChanged>());
    }

    [Fact]
    public void Name_that_fits_after_trimming_is_accepted()
    {
        var name = new string('a', Category.MaxNameLength);

        var category = ResultAssert.Success(Category.Create($"  {name}  ", "sen"));

        Assert.Equal(name, category.Name);
    }
}
