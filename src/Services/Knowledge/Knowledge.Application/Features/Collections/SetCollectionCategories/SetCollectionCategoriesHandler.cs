using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using FluentValidation;
using Knowledge.Domain.Categories;
using Knowledge.Domain.Collections;

namespace Knowledge.Application.Features.Collections.SetCollectionCategories;

/// <summary>
/// Handles <see cref="SetCollectionCategories"/>: loads the collection, verifies that all referenced categories exist and delegates
/// the replacement to <see cref="Collection.SetCategories"/>.
/// </summary>
/// <remarks>
/// Categories are another aggregate, so the collection stores only their identifiers; the existence check here is a point-in-time
/// check, not a foreign key guarantee. Identifiers that cannot be converted to <see cref="CategoryId"/> count as unknown categories.
/// </remarks>
internal sealed class SetCollectionCategoriesHandler(ICollectionRepository collections, ICategoryRepository categories, IClock clock)
    : ICommandHandler<SetCollectionCategories>
{
    /// <inheritdoc />
    public async Task<Result> Handle(SetCollectionCategories command, CancellationToken cancellationToken)
    {
        if (!CollectionId.Create(command.CollectionId).TryGetValue(out var collectionId, out _))
        {
            return CollectionErrors.NotFound;
        }

        var collection = await collections.GetAsync(collectionId, cancellationToken);
        if (collection is null)
        {
            return CollectionErrors.NotFound;
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

        return collection.SetCategories(categoryIds, clock.UtcNow);
    }
}
