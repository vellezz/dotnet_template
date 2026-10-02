using FluentValidation;
using Knowledge.Application.Validation;
using Knowledge.Domain.Categories;

namespace Knowledge.Application.Features.Categories.RenameCategory;

/// <summary>
/// Input rules of <see cref="RenameCategory"/>: a non-empty identifier and a required name within <see cref="Category.MaxNameLength"/>.
/// </summary>
/// <remarks>
/// Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400). Text lengths are measured after trimming, exactly as the aggregate measures them
/// (<see cref="Knowledge.Application.Validation.TextRuleExtensions.MaximumTrimmedLength{T}"/>), so the validator accepts the same values as the aggregate.
/// </remarks>
internal sealed class RenameCategoryValidator : AbstractValidator<RenameCategory>
{
    /// <summary>Defines the rules.</summary>
    public RenameCategoryValidator()
    {
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumTrimmedLength(Category.MaxNameLength);
    }
}
