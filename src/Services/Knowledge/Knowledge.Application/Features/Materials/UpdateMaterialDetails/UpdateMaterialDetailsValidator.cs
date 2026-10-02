using FluentValidation;
using Knowledge.Application.Validation;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Materials.UpdateMaterialDetails;

/// <summary>
/// Input rules of <see cref="UpdateMaterialDetails"/>: a non-empty identifier, a required title within <see cref="Material.MaxTitleLength"/>
/// and an optional description within <see cref="Material.MaxDescriptionLength"/>.
/// </summary>
/// <remarks>
/// Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400). Text lengths are measured after trimming, exactly as the aggregate measures them
/// (<see cref="Knowledge.Application.Validation.TextRuleExtensions.MaximumTrimmedLength{T}"/>), so the validator accepts the same values as the aggregate. Main media URL and duration are
/// checked by the aggregate because the rules depend on the material type and status.
/// </remarks>
internal sealed class UpdateMaterialDetailsValidator : AbstractValidator<UpdateMaterialDetails>
{
    /// <summary>Defines the rules.</summary>
    public UpdateMaterialDetailsValidator()
    {
        RuleFor(command => command.MaterialId).NotEmpty();
        RuleFor(command => command.Title).NotEmpty().MaximumTrimmedLength(Material.MaxTitleLength);
        RuleFor(command => command.Description).MaximumTrimmedLength(Material.MaxDescriptionLength);
    }
}
