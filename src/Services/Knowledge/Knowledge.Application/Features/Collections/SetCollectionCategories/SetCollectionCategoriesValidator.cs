using FluentValidation;

namespace Knowledge.Application.Features.Collections.SetCollectionCategories;

/// <summary>
/// Input rules of <see cref="SetCollectionCategories"/>: a non-empty collection identifier and a present list without empty identifiers.
/// </summary>
/// <remarks>
/// Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400). The maximum number of categories
/// is a business rule checked by the aggregate.
/// </remarks>
internal sealed class SetCollectionCategoriesValidator : AbstractValidator<SetCollectionCategories>
{
    /// <summary>Defines the rules.</summary>
    public SetCollectionCategoriesValidator()
    {
        RuleFor(command => command.CollectionId).NotEmpty();
        RuleFor(command => command.CategoryIds).NotNull();
        RuleForEach(command => command.CategoryIds).NotEmpty();
    }
}
