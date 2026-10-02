using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Categories;

namespace Knowledge.Application.Features.Categories.CreateCategory;

/// <summary>
/// Handles <see cref="CreateCategory"/>: checks slug uniqueness, creates the <see cref="Category"/> aggregate and adds it to the repository.
/// </summary>
/// <remarks>
/// Uniqueness is checked against saved categories only; the unique index on the slug column is the final guard when two requests race,
/// and the unit of work reports that violation as the same <see cref="CategoryErrors.SlugTaken"/> (the write context maps the index).
/// Saving happens in the transaction behavior after the handler returns success.
/// </remarks>
internal sealed class CreateCategoryHandler(ICategoryRepository categories) : ICommandHandler<CreateCategory, Result<Guid>>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(CreateCategory command, CancellationToken cancellationToken)
    {
        if (await categories.SlugExistsAsync(command.Slug, cancellationToken))
        {
            return CategoryErrors.SlugTaken;
        }

        if (!Category.Create(command.Name, command.Slug).TryGetValue(out var category, out var error))
        {
            return error;
        }

        categories.Add(category);
        return category.Id.Value;
    }
}
