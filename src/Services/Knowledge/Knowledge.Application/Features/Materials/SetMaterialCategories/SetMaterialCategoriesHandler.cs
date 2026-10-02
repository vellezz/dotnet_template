using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using FluentValidation;
using Knowledge.Domain.Categories;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Materials.SetMaterialCategories;

/// <summary>
/// Handles <see cref="SetMaterialCategories"/>: loads the material, verifies that all referenced categories exist and delegates the
/// replacement to <see cref="Material.SetCategories"/>.
/// </summary>
/// <remarks>
/// Categories are another aggregate, so the material stores only their identifiers; the existence check here is a point-in-time check,
/// not a foreign key guarantee. Identifiers that cannot be converted to <see cref="CategoryId"/> count as unknown categories.
/// </remarks>
internal sealed class SetMaterialCategoriesHandler(IMaterialRepository materials, ICategoryRepository categories, IClock clock)
    : ICommandHandler<SetMaterialCategories>
{
    /// <inheritdoc />
    public async Task<Result> Handle(SetMaterialCategories command, CancellationToken cancellationToken)
    {
        if (!MaterialId.Create(command.MaterialId).TryGetValue(out var materialId, out _))
        {
            return MaterialErrors.NotFound;
        }

        var material = await materials.GetAsync(materialId, cancellationToken);
        if (material is null)
        {
            return MaterialErrors.NotFound;
        }

        var categoryIds = new List<CategoryId>();
        foreach (var rawId in command.CategoryIds.Distinct())
        {
            if (!CategoryId.Create(rawId).TryGetValue(out var categoryId, out _))
            {
                return CategoryErrors.UnknownCategories;
            }

            categoryIds.Add(categoryId);
        }

        if (!await categories.AllExistAsync(categoryIds, cancellationToken))
        {
            return CategoryErrors.UnknownCategories;
        }

        return material.SetCategories(categoryIds, clock.UtcNow);
    }
}
