using FluentValidation;

namespace Knowledge.Application.Features.Library.AddFavorite;

/// <summary>Input rules of <see cref="AddFavorite"/>: a defined item type and a non-empty item identifier.</summary>
/// <remarks>Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400).</remarks>
internal sealed class AddFavoriteValidator : AbstractValidator<AddFavorite>
{
    /// <summary>Defines the rules.</summary>
    public AddFavoriteValidator()
    {
        RuleFor(command => command.ItemType).IsInEnum();
        RuleFor(command => command.ItemId).NotEmpty();
    }
}
