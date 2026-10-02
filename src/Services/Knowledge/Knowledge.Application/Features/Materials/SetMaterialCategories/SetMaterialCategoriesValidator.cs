using FluentValidation;

namespace Knowledge.Application.Features.Materials.SetMaterialCategories;

/// <summary>
/// Input rules of <see cref="SetMaterialCategories"/>: a non-empty material identifier and a present list without empty identifiers.
/// </summary>
/// <remarks>
/// Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400). The maximum number of categories
/// is a business rule checked by the aggregate.
/// </remarks>
internal sealed class SetMaterialCategoriesValidator : AbstractValidator<SetMaterialCategories>
{
    /// <summary>Defines the rules.</summary>
    public SetMaterialCategoriesValidator()
    {
        RuleFor(command => command.MaterialId).NotEmpty();
        RuleFor(command => command.CategoryIds).NotNull();
        RuleForEach(command => command.CategoryIds).NotEmpty();
    }
}
