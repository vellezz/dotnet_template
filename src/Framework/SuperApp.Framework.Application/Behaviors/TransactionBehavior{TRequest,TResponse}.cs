using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Persistence;
using SuperApp.Framework.Domain.Results;
using MediatR;

namespace SuperApp.Framework.Application.Behaviors;

/// <summary>
/// Fourth (innermost) pipeline behavior, applied to commands only: runs the handler in a database transaction and,
/// when the result is successful, saves all changes through the unit of work and commits (ADR-0005, ADR-0017, ADR-0027).
/// </summary>
/// <remarks>
/// A failed result, a failed save (unique index conflict, returned as a <c>Conflict</c> error) or an exception leaves the transaction
/// uncommitted, so it is rolled back on dispose. When a transaction is already open (a MassTransit consumer with the EF outbox),
/// the behavior saves within it and leaves the commit to the consumer pipeline; a failed result then discards the command's tracked
/// changes (<see cref="IUnitOfWork.DiscardChanges"/>), because the consumer pipeline still saves and commits its inbox state afterwards.
/// </remarks>
internal sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
    where TResponse : IResultFactory<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (unitOfWork.HasActiveTransaction)
        {
            var nested = await next();
            if (!IsSuccess(nested))
            {
                unitOfWork.DiscardChanges();
                return nested;
            }

            var savedNested = await unitOfWork.SaveChangesAsync(cancellationToken);
            return savedNested.IsSuccess ? nested : TResponse.FromError(savedNested.Error);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var response = await next();
        if (!IsSuccess(response))
        {
            return response;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return TResponse.FromError(saved.Error);
        }

        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static bool IsSuccess(TResponse response) => response is Result { IsSuccess: true };
}
