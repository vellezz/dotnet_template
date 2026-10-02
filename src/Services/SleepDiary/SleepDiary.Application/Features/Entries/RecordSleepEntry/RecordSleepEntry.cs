using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;

namespace SleepDiary.Application.Features.Entries.RecordSleepEntry;

/// <summary>
/// Command: records a new sleep diary entry for the current user and day, and publishes <see cref="Contracts.SleepEntryRecordedV1"/>.
/// </summary>
/// <remarks>
/// <para>
/// Requires the scope <see cref="SleepDiaryScopes.EntryWrite"/>. The owner is the caller's <c>sub</c>; a user has at most one entry per day.
/// Time in bed and sleep time are computed by the <see cref="Domain.Entries.SleepEntry"/> aggregate. The integration event is written to the
/// outbox in the same transaction as the entry.
/// </para>
/// <para>Errors, in the order they are checked:</para>
/// <list type="number">
///   <item><description>Pipeline: <c>auth.missing_scope</c> / <c>auth.unauthenticated</c> (HTTP 403).</description></item>
///   <item><description><c>RecordSleepEntryValidator</c>: <c>validation.failed</c> (HTTP 400) with field messages when <see cref="BedTime"/> or
///   <see cref="WakeTime"/> carries a time zone (<see cref="DateTime.Kind"/> other than <see cref="DateTimeKind.Unspecified"/>, i.e. it was
///   sent with <c>Z</c> or an offset), when <see cref="Quality"/>, <see cref="Awakenings"/> or <see cref="SleepLatencyMinutes"/> is out of
///   range, or when <see cref="Notes"/> is too long after trimming.</description></item>
///   <item><description><see cref="Domain.Entries.SleepEntryErrors.AlreadyExists"/> (<c>sleepdiary.entry.already_exists</c>, HTTP 409):
///   the user already has an entry for <see cref="Date"/>. Also returned when a concurrent request recorded the same day first: the unique
///   index violation on save is mapped to this error by the write context.</description></item>
///   <item><description><see cref="Domain.Entries.SleepEntryErrors.FutureDate"/> (<c>sleepdiary.entry.future_date</c>, HTTP 400):
///   <see cref="Date"/> is later than today in UTC plus one day (one day of tolerance for time zones ahead of UTC).</description></item>
///   <item><description>Any other rule of <see cref="Domain.Entries.SleepEntry.Record"/>, for example <c>sleepdiary.entry.wake_before_bed</c>,
///   <c>sleepdiary.entry.wake_date_mismatch</c>, <c>sleepdiary.entry.too_long</c> or <c>sleepdiary.entry.invalid_latency</c> (HTTP 400).</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var result = await sender.Send(
///     new RecordSleepEntry(
///         Date: new DateOnly(2026, 9, 30),
///         BedTime: new DateTime(2026, 9, 29, 23, 0, 0),
///         WakeTime: new DateTime(2026, 9, 30, 7, 0, 0),
///         SleepLatencyMinutes: 15,
///         Awakenings: 1,
///         Quality: 4,
///         Notes: null),
///     cancellationToken);
/// // result.Value is the new entry's ID
/// </code>
/// </example>
/// <param name="Date">Entry date: the day the user woke up, in the user's local calendar; at most one day after today in UTC.</param>
/// <param name="BedTime">
/// When the user went to bed: local date and time of the user, without a time zone (<see cref="DateTimeKind.Unspecified"/>; values with
/// <see cref="DateTimeKind.Utc"/> or <see cref="DateTimeKind.Local"/> are rejected by the validator).
/// </param>
/// <param name="WakeTime">
/// When the user got up: local date and time of the user, without a time zone (<see cref="DateTimeKind.Unspecified"/>, like
/// <paramref name="BedTime"/>). Must be later than <paramref name="BedTime"/>, on the day
/// <paramref name="Date"/>, and at most 24 hours after <paramref name="BedTime"/>.
/// </param>
/// <param name="SleepLatencyMinutes">How long it took to fall asleep, in minutes; from 0 up to the time spent in bed.</param>
/// <param name="Awakenings">Number of awakenings during the night, from 0 to 50.</param>
/// <param name="Quality">Subjective sleep quality, from 1 (worst) to 5 (best).</param>
/// <param name="Notes">
/// Optional note of at most 2000 characters after trimming leading and trailing whitespace; <see langword="null"/>, empty or whitespace-only
/// text means no note. Stored trimmed.
/// </param>
[RequiresScope(SleepDiaryScopes.EntryWrite)]
public sealed record RecordSleepEntry(
    DateOnly Date,
    DateTime BedTime,
    DateTime WakeTime,
    int SleepLatencyMinutes,
    int Awakenings,
    int Quality,
    string? Notes) : ICommand<Result<Guid>>;
