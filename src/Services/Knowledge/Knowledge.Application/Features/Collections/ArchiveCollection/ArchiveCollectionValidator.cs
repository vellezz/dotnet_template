using FluentValidation;

namespace Knowledge.Application.Features.Collections.ArchiveCollection;

/// <summary>Input rules of <see cref="ArchiveCollection"/>: the collection identifier must not be empty.</summary>
/// <remarks>Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400).</remarks>
internal sealed class ArchiveCollectionValidator : AbstractValidator<ArchiveCollection>
{
    /// <summary>Defines the rules.</summary>
    public ArchiveCollectionValidator() => RuleFor(command => command.CollectionId).NotEmpty();
}
