using FluentValidation;

namespace Knowledge.Application.Features.Library.UnmarkMaterialCompleted;

/// <summary>Input rules of <see cref="UnmarkMaterialCompleted"/>: the material identifier must not be empty.</summary>
/// <remarks>Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400).</remarks>
internal sealed class UnmarkMaterialCompletedValidator : AbstractValidator<UnmarkMaterialCompleted>
{
    /// <summary>Defines the rules.</summary>
    public UnmarkMaterialCompletedValidator() => RuleFor(command => command.MaterialId).NotEmpty();
}
