using FluentValidation;

namespace Knowledge.Application.Features.Library.RemoveFavoritesOfItem;

/// <summary>Input rules of <see cref="RemoveFavoritesOfItem"/>: a defined item type and a non-empty item identifier.</summary>
/// <remarks>Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400).</remarks>
internal sealed class RemoveFavoritesOfItemValidator : AbstractValidator<RemoveFavoritesOfItem>
{
    /// <summary>Defines the rules.</summary>
    public RemoveFavoritesOfItemValidator()
    {
        RuleFor(command => command.ItemType).IsInEnum();
        RuleFor(command => command.ItemId).NotEmpty();
    }
}
