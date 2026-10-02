using FluentValidation;

namespace Knowledge.Application.Features.Collections.SetCollectionItems;

/// <summary>
/// Input rules of <see cref="SetCollectionItems"/>: a non-empty collection identifier and a present list without empty identifiers.
/// </summary>
/// <remarks>
/// Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400). The item limit and the duplicate
/// rule are business rules checked by the aggregate.
/// </remarks>
internal sealed class SetCollectionItemsValidator : AbstractValidator<SetCollectionItems>
{
    /// <summary>Defines the rules.</summary>
    public SetCollectionItemsValidator()
    {
        RuleFor(command => command.CollectionId).NotEmpty();
        RuleFor(command => command.MaterialIds).NotNull();
        RuleForEach(command => command.MaterialIds).NotEmpty();
    }
}
