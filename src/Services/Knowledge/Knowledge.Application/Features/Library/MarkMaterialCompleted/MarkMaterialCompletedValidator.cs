using FluentValidation;

namespace Knowledge.Application.Features.Library.MarkMaterialCompleted;

/// <summary>Input rules of <see cref="MarkMaterialCompleted"/>: the material identifier must not be empty.</summary>
/// <remarks>Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400).</remarks>
internal sealed class MarkMaterialCompletedValidator : AbstractValidator<MarkMaterialCompleted>
{
    /// <summary>Defines the rules.</summary>
    public MarkMaterialCompletedValidator() => RuleFor(command => command.MaterialId).NotEmpty();
}
