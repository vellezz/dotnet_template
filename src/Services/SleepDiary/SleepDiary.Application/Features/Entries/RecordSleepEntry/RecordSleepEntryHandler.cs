using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using SleepDiary.Domain.Entries.Events;
using SleepDiary.Domain.Entries;

namespace SleepDiary.Application.Features.Entries.RecordSleepEntry;

/// <summary>
/// Handles <see cref="RecordSleepEntry"/>: checks the one-entry-per-day rule, lets the aggregate create the entry and adds it to the repository.
/// </summary>
/// <remarks>
/// <para>
/// The handler only orchestrates; all entry rules live in <see cref="SleepEntry.Record"/>. The entry, its <see cref="SleepEntryRecorded"/>
/// domain event and the resulting outbox message are saved together by the transaction pipeline behavior.
/// </para>
/// <para>
/// The duplicate check (<see cref="ISleepEntryRepository.ExistsAsync"/>) and the insert are not atomic. Two concurrent requests for the same day
/// can both pass the check; the unique index on <c>UserId</c> + <c>Date</c> then rejects the second insert when the transaction behavior
/// saves the changes. The write context maps that index violation to the same <see cref="SleepEntryErrors.AlreadyExists"/>
/// (<c>sleepdiary.entry.already_exists</c>, HTTP 409), so the caller gets the same answer either way.
/// </para>
/// </remarks>
/// <param name="entries">Write-side repository of diary entries.</param>
/// <param name="currentUser">The caller; the owner of the entry is always taken from it.</param>
/// <param name="clock">Source of the current UTC time, for the future-date rule and the audit timestamps.</param>
internal sealed class RecordSleepEntryHandler(ISleepEntryRepository entries, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RecordSleepEntry, Result<Guid>>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(RecordSleepEntry command, CancellationToken cancellationToken)
    {
        if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
        {
            return userError;
        }

        if (await entries.ExistsAsync(userId, command.Date, cancellationToken))
        {
            return SleepEntryErrors.AlreadyExists;
        }

        // One day of tolerance for time zones: the service does not know the user's time zone (ADR-0029).
        var latestAllowedDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(1);
        var details = new SleepDetails(command.BedTime, command.WakeTime, command.SleepLatencyMinutes, command.Awakenings, command.Quality, command.Notes);
        if (!SleepEntry.Record(userId, command.Date, details, latestAllowedDate, clock.UtcNow).TryGetValue(out var entry, out var error))
        {
            return error;
        }

        entries.Add(entry);
        return entry.Id.Value;
    }
}
