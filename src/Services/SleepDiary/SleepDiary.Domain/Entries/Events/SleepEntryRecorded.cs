using SuperApp.Framework.Domain.Events;

namespace SleepDiary.Domain.Entries.Events;

/// <summary>
/// Domain event: a user recorded a new sleep diary entry. Raised only by <see cref="SleepEntry.Record"/>; updates and deletions raise no event.
/// </summary>
/// <remarks>
/// The event is dispatched by the unit of work when the command's changes are saved, in the same database transaction (ADR-0027).
/// Its handler <c>SleepEntryRecordedTranslator</c> (Application) turns it into the public integration event
/// <c>SleepDiary.Contracts.SleepEntryRecordedV1</c>, written to the outbox. Being internal, this event may change freely; the integration event may not.
/// The values are a snapshot at the moment of recording and do not follow later updates of the entry.
/// </remarks>
/// <param name="EntryId">Identifier of the new entry.</param>
/// <param name="UserId">Owner of the entry (CIAM <c>sub</c>).</param>
/// <param name="Date">Entry date: the day the user woke up, in the user's local calendar.</param>
/// <param name="SleepMinutes">Derived sleep time in minutes: time in bed minus the time needed to fall asleep.</param>
/// <param name="Quality">Sleep quality rating, from <see cref="SleepQuality.Min"/> to <see cref="SleepQuality.Max"/>.</param>
/// <param name="RecordedAt">
/// UTC instant the entry was recorded: the entry's <see cref="SleepEntry.CreatedAt"/>, taken from the service clock by the command handler.
/// The translator copies it to the integration event, so the event carries the aggregate's creation time rather than the time the event
/// happened to be translated.
/// </param>
/// <seealso cref="SleepEntry.Record"/>
public sealed record SleepEntryRecorded(
    SleepEntryId EntryId,
    UserId UserId,
    DateOnly Date,
    int SleepMinutes,
    int Quality,
    DateTimeOffset RecordedAt) : IDomainEvent;
