namespace SleepDiary.Contracts;

// Published language of the SleepDiary service (ADR-0005): primitive types only; a breaking change means a new version of the type.

/// <summary>
/// Integration event, version 1: a user recorded a new sleep diary entry. Published by the SleepDiary service for other bounded contexts.
/// </summary>
/// <remarks>
/// <para>
/// Published once per newly recorded entry. It is NOT published when an entry is updated or deleted (ADR-0029), so a consumer that keeps
/// its own copy of the values cannot follow later corrections; treat the values as a snapshot at the moment of recording.
/// </para>
/// <para>
/// The event is written to the transactional outbox in the same database transaction as the entry itself and delivered to RabbitMQ
/// after commit by the SleepDiary Worker: it is published if and only if the entry was saved. Delivery is at least once, so consumers
/// must be idempotent; use <paramref name="EntryId"/> as the deduplication key.
/// </para>
/// <para>
/// Compatibility rules: this type is a public, versioned contract. Only backward-compatible changes are allowed (for example adding an
/// optional property); renaming, removing or changing the meaning of a property requires a new type <c>SleepEntryRecordedV2</c>.
/// </para>
/// </remarks>
/// <example>
/// A consumer in another service (MassTransit), delegating to a command of its own context:
/// <code>
/// internal sealed class SleepEntryRecordedConsumer(ISender sender) : IConsumer&lt;SleepEntryRecordedV1&gt;
/// {
///     public async Task Consume(ConsumeContext&lt;SleepEntryRecordedV1&gt; context) =&gt;
///         await sender.Send(new RegisterNight(context.Message.EntryId, context.Message.UserId, context.Message.Date), context.CancellationToken);
/// }
/// </code>
/// </example>
/// <param name="EntryId">Identifier of the diary entry (a GUID version 7); unique, suitable as the idempotency key.</param>
/// <param name="UserId">Owner of the entry: the user's <c>sub</c> claim from the CIAM, an opaque string of at most 200 characters.</param>
/// <param name="Date">Entry date: the day the user woke up, in the user's local calendar (no time zone information is available).</param>
/// <param name="SleepMinutes">
/// Sleep time in whole minutes at the moment of recording: time in bed minus the time needed to fall asleep; between 0 and 1440.
/// </param>
/// <param name="Quality">Subjective sleep quality at the moment of recording, from 1 (worst) to 5 (best).</param>
/// <param name="RecordedAt">
/// UTC instant the entry was recorded: the entry's creation time stored by the SleepDiary service, taken from the service clock when the
/// recording command ran. It is not the moment of delivery.
/// </param>
public sealed record SleepEntryRecordedV1(Guid EntryId, string UserId, DateOnly Date, int SleepMinutes, int Quality, DateTimeOffset RecordedAt);
