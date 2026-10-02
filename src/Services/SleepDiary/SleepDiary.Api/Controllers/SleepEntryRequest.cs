namespace SleepDiary.Api.Controllers;

/// <summary>
/// Data of one night in the sleep diary, sent when recording (<c>POST</c>) or replacing (<c>PUT</c>) an entry. The entry date is taken from the URL.
/// </summary>
/// <remarks>
/// Times are the user's local wall-clock times: send them as ISO 8601 date-times without an offset or <c>Z</c>
/// (for example <c>2026-09-29T23:15:00</c>). The service does not know the user's time zone and stores the values as sent, to the second.
/// A value with <c>Z</c> or an offset (for example <c>2026-09-29T23:15:00+02:00</c>) is rejected with <c>validation.failed</c> (HTTP 400),
/// because it would otherwise be converted and shift the wall-clock time.
/// All durations are whole minutes. Time in bed and sleep time are computed by the service and must not be sent.
/// </remarks>
/// <param name="BedTime">
/// When the user went to bed, local date and time without an offset, e.g. <c>2026-09-29T23:15:00</c>; a value with <c>Z</c> or an offset
/// is rejected (HTTP 400).
/// </param>
/// <param name="WakeTime">
/// When the user got up, local date and time without an offset (a value with <c>Z</c> or an offset is rejected, HTTP 400). Must be later than <paramref name="BedTime"/>, on the same calendar day as the
/// entry date in the URL, and at most 24 hours after <paramref name="BedTime"/>.
/// </param>
/// <param name="SleepLatencyMinutes">How long it took to fall asleep, in minutes; from 0 up to the time between bedtime and wake-up.</param>
/// <param name="Awakenings">Number of times the user woke up during the night, from 0 to 50.</param>
/// <param name="Quality">Subjective sleep quality, an integer from 1 (worst) to 5 (best).</param>
/// <param name="Notes">
/// Optional note of at most 2000 characters after removing leading and trailing whitespace, and stored that way. <c>null</c>, empty or whitespace-only text means
/// no note; on <c>PUT</c> it removes an existing note.
/// </param>
public sealed record SleepEntryRequest(
    DateTime BedTime,
    DateTime WakeTime,
    int SleepLatencyMinutes,
    int Awakenings,
    int Quality,
    string? Notes);
