using FluentValidation;

namespace Knowledge.Application.Features.Library.RemoveFavorite;

/// <summary>Input rules of <see cref="RemoveFavorite"/>: a defined item type and a non-empty item identifier.</summary>
/// <remarks>Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400).</remarks>
internal sealed class RemoveFavoriteValidator : AbstractValidator<RemoveFavorite>
{
    /// <summary>Defines the rules.</summary>
    public RemoveFavoriteValidator()
    {
        RuleFor(command => command.ItemType).IsInEnum();
        RuleFor(command => command.ItemId).NotEmpty();
    }
}
