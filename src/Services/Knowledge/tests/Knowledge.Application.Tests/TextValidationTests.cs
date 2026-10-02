using Knowledge.Application.Features.Categories.CreateCategory;
using Knowledge.Application.Features.Categories.RenameCategory;
using Knowledge.Application.Features.Collections.CreateCollection;
using Knowledge.Application.Features.Collections.UpdateCollectionDetails;
using Knowledge.Application.Features.Materials.CreateMaterial;
using Knowledge.Application.Features.Materials.UpdateMaterialDetails;
using Knowledge.Domain.Categories;
using Knowledge.Domain.Collections;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Tests;

/// <summary>Validators measure text lengths after trimming, with the limits of the aggregates, so they accept exactly what the domain accepts.</summary>
public sealed class TextValidationTests
{
    private static string Padded(int length) => $"   {new string('a', length)}   ";

    [Fact]
    public void Material_title_and_description_that_fit_after_trimming_are_accepted()
    {
        var command = new CreateMaterial(MaterialType.Article, Padded(Material.MaxTitleLength), Padded(Material.MaxDescriptionLength), null, null);

        Assert.True(new CreateMaterialValidator().Validate(command).IsValid);
        Assert.True(Material.Create(command.Type, command.Title, command.Description, null, null, DateTimeOffset.UnixEpoch).IsSuccess);
    }

    [Fact]
    public void Material_title_longer_than_the_limit_after_trimming_is_rejected()
    {
        var validator = new UpdateMaterialDetailsValidator();

        var tooLong = validator.Validate(new UpdateMaterialDetails(Guid.NewGuid(), Padded(Material.MaxTitleLength + 1), null, null, null));
        var blank = validator.Validate(new UpdateMaterialDetails(Guid.NewGuid(), "   ", null, null, null));
        var description = validator.Validate(new UpdateMaterialDetails(Guid.NewGuid(), "Tytuł", Padded(Material.MaxDescriptionLength + 1), null, null));

        Assert.Contains(tooLong.Errors, error => error.PropertyName == nameof(UpdateMaterialDetails.Title));
        Assert.Contains(blank.Errors, error => error.PropertyName == nameof(UpdateMaterialDetails.Title));
        Assert.Contains(description.Errors, error => error.PropertyName == nameof(UpdateMaterialDetails.Description));
    }

    [Fact]
    public void Collection_texts_follow_the_collection_limits_after_trimming()
    {
        Assert.True(new CreateCollectionValidator().Validate(new CreateCollection(Padded(Collection.MaxTitleLength), Padded(Collection.MaxDescriptionLength))).IsValid);
        Assert.False(new UpdateCollectionDetailsValidator().Validate(
            new UpdateCollectionDetails(Guid.NewGuid(), Padded(Collection.MaxTitleLength + 1), null)).IsValid);
    }

    [Fact]
    public void Category_name_follows_the_category_limit_after_trimming()
    {
        Assert.True(new CreateCategoryValidator().Validate(new CreateCategory(Padded(Category.MaxNameLength), "sen")).IsValid);
        Assert.True(new RenameCategoryValidator().Validate(new RenameCategory(Guid.NewGuid(), Padded(Category.MaxNameLength))).IsValid);
        Assert.False(new RenameCategoryValidator().Validate(new RenameCategory(Guid.NewGuid(), Padded(Category.MaxNameLength + 1))).IsValid);
    }
}
