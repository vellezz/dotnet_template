using FluentValidation;
using Knowledge.Application.Validation;
using Knowledge.Domain.Collections;

namespace Knowledge.Application.Features.Collections.UpdateCollectionDetails;

/// <summary>
/// Input rules of <see cref="UpdateCollectionDetails"/>: a non-empty identifier, a required title within
/// <see cref="Collection.MaxTitleLength"/> and an optional description within <see cref="Collection.MaxDescriptionLength"/>.
/// </summary>
/// <remarks>
/// Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400). Text lengths are measured after trimming, exactly as the aggregate measures them
/// (<see cref="Knowledge.Application.Validation.TextRuleExtensions.MaximumTrimmedLength{T}"/>), so the validator accepts the same values as the aggregate.
/// </remarks>
internal sealed class UpdateCollectionDetailsValidator : AbstractValidator<UpdateCollectionDetails>
{
    /// <summary>Defines the rules.</summary>
    public UpdateCollectionDetailsValidator()
    {
        RuleFor(command => command.CollectionId).NotEmpty();
        RuleFor(command => command.Title).NotEmpty().MaximumTrimmedLength(Collection.MaxTitleLength);
        RuleFor(command => command.Description).MaximumTrimmedLength(Collection.MaxDescriptionLength);
    }
}
