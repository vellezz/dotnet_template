using Knowledge.Domain.Library.Favorites;
using Knowledge.Application.Features.Library.RemoveFavoritesOfItem;
using Knowledge.Contracts;
using MassTransit;
using MediatR;

namespace Knowledge.Worker.Consumers;

/// <summary>
/// Consumes <see cref="MaterialArchivedV1"/> and removes the archived material from the favorites of all users by sending the command
/// <see cref="RemoveFavoritesOfItem"/> with <see cref="FavoriteItemType.Material"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the asynchronous half of archiving a material: the archive command changes only the Material aggregate and publishes the event through
/// the outbox; this consumer then deletes all <c>Favorite</c> rows pointing to the material in one statement. Deleting many <c>Favorite</c>
/// aggregates at once is a deliberate exception to the "one transaction modifies one aggregate" rule (ADR-0028), acceptable because it
/// breaks no invariant of any of them. Until the event is processed, the favorites list already hides archived items.
/// </para>
/// <para>
/// The consumer is thin (event to command, ADR-0005). The command runs through the full MediatR pipeline as the worker's system user,
/// which has every scope, so the <c>knowledge.catalog.write</c> requirement of the command is met.
/// </para>
/// <para>
/// <b>Idempotency:</b> the endpoint uses the MassTransit Entity Framework outbox/inbox on <c>KnowledgeWriteDbContext</c>, so a redelivered
/// message with the same message ID is skipped; in addition, deleting favorites of an item that has none is a no-op, so processing the same
/// event twice is harmless.
/// </para>
/// <para>
/// <b>Failures:</b> a business rejection (a failed <c>Result</c>) is logged as a warning (event ID 2001) and the message is acknowledged,
/// because retrying would not change the outcome. A technical exception (database unavailable, timeout) is retried by the endpoint retry
/// policy and, when retries are exhausted, the message is moved to the <c>_error</c> queue (ADR-0015).
/// </para>
/// </remarks>
/// <param name="sender">MediatR sender used to send the command to the Application layer.</param>
/// <param name="logger">Logger for rejected commands.</param>
public sealed partial class MaterialArchivedConsumer(ISender sender, ILogger<MaterialArchivedConsumer> logger) : IConsumer<MaterialArchivedV1>
{
    /// <summary>Handles one delivery of the event by removing the archived material from all favorites.</summary>
    /// <param name="context">The consumed message with its headers; its cancellation token is passed to the command.</param>
    /// <returns>A task that completes when the command has finished; a thrown exception triggers the retry policy.</returns>
    public async Task Consume(ConsumeContext<MaterialArchivedV1> context)
    {
        var result = await sender.Send(new RemoveFavoritesOfItem(FavoriteItemType.Material, context.Message.MaterialId), context.CancellationToken);
        if (result.IsFailure)
        {
            LogRejected(logger, context.Message.MaterialId, result.Error.Code);
        }
    }

    // Source-generated log method (the only allowed way of logging, see architecture rules §11).
    [LoggerMessage(2001, LogLevel.Warning, "Removing favorites of archived material {MaterialId} rejected: {ErrorCode}")]
    private static partial void LogRejected(ILogger logger, Guid materialId, string errorCode);
}
