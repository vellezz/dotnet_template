using SuperApp.Framework.Application.Events;
using SleepDiary.Domain.Entries.Events;
using SleepDiary.Contracts;

namespace SleepDiary.Application.IntegrationEvents;

/// <summary>
/// Translates the internal domain event <see cref="SleepEntryRecorded"/> into the public integration event <see cref="SleepEntryRecordedV1"/>
/// and hands it to the outbox (ADR-0027).
/// </summary>
/// <remarks>
/// Invoked by the unit of work while the recording command's changes are being saved, inside the same transaction. <see cref="IIntegrationEventPublisher"/>
/// only writes the message to the outbox table; the Worker delivers it to RabbitMQ after commit. Strongly typed values are converted to
/// primitives here, so domain types never leak into the contract.
/// <see cref="SleepEntryRecordedV1.RecordedAt"/> is copied from <see cref="SleepEntryRecorded.RecordedAt"/> (the entry's creation time set by
/// the aggregate), not read from the clock, so the published instant always equals the entry's stored <c>CreatedAt</c>.
/// </remarks>
/// <param name="publisher">Outbox-backed publisher of integration events.</param>
internal sealed class SleepEntryRecordedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<SleepEntryRecorded>
{
    /// <inheritdoc />
    public Task HandleAsync(SleepEntryRecorded domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            new SleepEntryRecordedV1(
                domainEvent.EntryId.Value, domainEvent.UserId.Value, domainEvent.Date, domainEvent.SleepMinutes, domainEvent.Quality, domainEvent.RecordedAt),
            cancellationToken);
}
