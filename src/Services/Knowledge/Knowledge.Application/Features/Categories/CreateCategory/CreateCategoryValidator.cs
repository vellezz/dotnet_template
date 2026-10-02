using FluentValidation;
using Knowledge.Application.Validation;
using Knowledge.Domain.Categories;

namespace Knowledge.Application.Features.Categories.CreateCategory;

/// <summary>
/// Input rules of <see cref="CreateCategory"/>: name and slug are required and within the length limits of <see cref="Category"/>.
/// </summary>
/// <remarks>
/// Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400). Text lengths are measured after trimming, exactly as the aggregate measures them
/// (<see cref="Knowledge.Application.Validation.TextRuleExtensions.MaximumTrimmedLength{T}"/>), so the validator accepts the same values as the aggregate. The slug format and uniqueness
/// are business rules checked by the aggregate and the handler.
/// </remarks>
internal sealed class CreateCategoryValidator : AbstractValidator<CreateCategory>
{
    /// <summary>Defines the rules.</summary>
    public CreateCategoryValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumTrimmedLength(Category.MaxNameLength);
        RuleFor(command => command.Slug).NotEmpty().MaximumLength(Category.MaxSlugLength);
    }
}
