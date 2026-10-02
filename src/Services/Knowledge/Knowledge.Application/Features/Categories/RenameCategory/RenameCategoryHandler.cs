using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Categories;

namespace Knowledge.Application.Features.Categories.RenameCategory;

/// <summary>
/// Handles <see cref="RenameCategory"/>: loads the category and delegates the change to <see cref="Category.Rename"/>.
/// </summary>
/// <remarks>An identifier that cannot be converted to <see cref="CategoryId"/> is reported as <see cref="CategoryErrors.NotFound"/>.</remarks>
internal sealed class RenameCategoryHandler(ICategoryRepository categories) : ICommandHandler<RenameCategory>
{
    /// <inheritdoc />
    public async Task<Result> Handle(RenameCategory command, CancellationToken cancellationToken)
    {
        if (!CategoryId.Create(command.CategoryId).TryGetValue(out var categoryId, out _))
        {
            return CategoryErrors.NotFound;
        }

        var category = await categories.GetAsync(categoryId, cancellationToken);
        return category is null ? CategoryErrors.NotFound : category.Rename(command.Name);
    }
}
