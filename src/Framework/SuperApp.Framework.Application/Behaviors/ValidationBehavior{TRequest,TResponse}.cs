using SuperApp.Framework.Domain.Results;
using FluentValidation;
using MediatR;

namespace SuperApp.Framework.Application.Behaviors;

/// <summary>
/// Third pipeline behavior: runs every FluentValidation validator registered for the request and, if any rule fails,
/// returns a <c>validation.failed</c> error with field messages instead of calling the handler (ADR-0015, ADR-0017).
/// </summary>
/// <remarks>
/// Validators check the shape of the input (required fields, lengths, ranges) so that clients get all field errors at once.
/// Business rules that depend on state stay in the aggregate. Validation failures never throw.
/// </remarks>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResultFactory<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return await next();
        }

        var details = failures
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());

        return TResponse.FromError(Error.Validation("validation.failed", "Żądanie zawiera niepoprawne dane.", details));
    }
}
