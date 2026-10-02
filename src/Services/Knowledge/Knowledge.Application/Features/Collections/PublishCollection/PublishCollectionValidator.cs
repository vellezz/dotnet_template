using FluentValidation;

namespace Knowledge.Application.Features.Collections.PublishCollection;

/// <summary>Input rules of <see cref="PublishCollection"/>: the collection identifier must not be empty.</summary>
/// <remarks>Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400).</remarks>
internal sealed class PublishCollectionValidator : AbstractValidator<PublishCollection>
{
    /// <summary>Defines the rules.</summary>
    public PublishCollectionValidator() => RuleFor(command => command.CollectionId).NotEmpty();
}
