using FluentValidation;

namespace Knowledge.Application.Features.Materials.ReplaceMaterialContent;

/// <summary>Input rules of <see cref="ReplaceMaterialContent"/>: a non-empty material identifier and a present (possibly empty) block list.</summary>
/// <remarks>
/// Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400). Content rules are deliberately
/// not duplicated here: the aggregate is their single owner and reports them as <c>knowledge.content.invalid_block</c>.
/// </remarks>
internal sealed class ReplaceMaterialContentValidator : AbstractValidator<ReplaceMaterialContent>
{
    /// <summary>Defines the rules.</summary>
    public ReplaceMaterialContentValidator()
    {
        RuleFor(command => command.MaterialId).NotEmpty();
        RuleFor(command => command.Blocks).NotNull();
    }
}
