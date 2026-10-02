using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace SleepDiary.Application.Features.Entries.UpdateSleepEntry;

/// <summary>
/// Command: replaces all data of the current user's existing sleep diary entry for the given day.
/// </summary>
/// <remarks>
/// <para>
/// Requires the scope <see cref="SleepDiaryScopes.EntryWrite"/>. This is a full replacement: every field is overwritten, and a
/// <see langword="null"/> or blank <see cref="Notes"/> removes the existing note. The entry date cannot be changed (delete the entry and record
/// a new one instead). Time in bed and sleep time are recomputed. No integration event is published for an update.
/// </para>
/// <para>Errors, in the order they are checked:</para>
/// <list type="number">
///   <item><description>Pipeline: <c>auth.missing_scope</c> / <c>auth.unauthenticated</c> (HTTP 403).</description></item>
///   <item><description><c>UpdateSleepEntryValidator</c>: <c>validation.failed</c> (HTTP 400) with field messages when <see cref="BedTime"/> or
///   <see cref="WakeTime"/> carries a time zone (<see cref="DateTime.Kind"/> other than <see cref="DateTimeKind.Unspecified"/>, i.e. it was
///   sent with <c>Z</c> or an offset), when <see cref="Quality"/>, <see cref="Awakenings"/> or <see cref="SleepLatencyMinutes"/> is out of
///   range, or when <see cref="Notes"/> is too long after trimming.</description></item>
///   <item><description><see cref="Domain.Entries.SleepEntryErrors.NotFound"/> (<c>sleepdiary.entry.not_found</c>, HTTP 404): the user has no
///   entry for <see cref="Date"/>.</description></item>
///   <item><description>Any rule of <see cref="Domain.Entries.SleepEntry.Update"/>, for example <c>sleepdiary.entry.wake_before_bed</c>,
///   <c>sleepdiary.entry.wake_date_mismatch</c>, <c>sleepdiary.entry.too_long</c> or <c>sleepdiary.entry.invalid_latency</c> (HTTP 400).
///   The entry is then left unchanged.</description></item>
/// </list>
/// </remarks>
/// <param name="Date">Date of the entry to update: the day the user woke up. Identifies the entry; it is not changed.</param>
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
/// Optional note of at most 2000 characters after trimming leading and trailing whitespace, stored trimmed; <see langword="null"/>, empty or
/// whitespace-only text removes the note.
/// </param>
[RequiresScope(SleepDiaryScopes.EntryWrite)]
public sealed record UpdateSleepEntry(
    DateOnly Date,
    DateTime BedTime,
    DateTime WakeTime,
    int SleepLatencyMinutes,
    int Awakenings,
    int Quality,
    string? Notes) : ICommand;
