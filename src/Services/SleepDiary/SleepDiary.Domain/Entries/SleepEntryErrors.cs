using SuperApp.Framework.Domain.Results;

namespace SleepDiary.Domain.Entries;

/// <summary>
/// Catalog of the business errors of the <see cref="SleepEntry"/> aggregate and its use cases (ADR-0015, ADR-0029).
/// </summary>
/// <remarks>
/// <para>
/// Each field is a single shared <see cref="Error"/> instance, so the aggregate, the handlers, the tests and the API documentation all refer
/// to one definition. The <see cref="Error.Code"/> values (<c>sleepdiary.entry.*</c>) are part of the public API contract: clients receive
/// them in the <c>code</c> extension of the <c>ProblemDetails</c> response and may branch on them, so never change or reuse a code.
/// The messages are shown as the <c>title</c> of the response and may change.
/// </para>
/// <para>
/// Two related codes are defined next to their value objects rather than here: <c>sleepdiary.entry.invalid_quality</c>
/// (<see cref="SleepQuality.Create"/>) and <c>sleepdiary.user.invalid_id</c> (<see cref="UserId.Create"/>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// if (await entries.ExistsAsync(userId, command.Date, cancellationToken)) // userId read with TryGetValue
/// {
///     return SleepEntryErrors.AlreadyExists;
/// }
/// </code>
/// </example>
/// <seealso cref="SleepEntry"/>
public static class SleepEntryErrors
{
    /// <summary>
    /// The current user has no entry for the requested day (<c>sleepdiary.entry.not_found</c>, NotFound, HTTP 404).
    /// Returned by the get, update and delete use cases; entries of other users are never visible, so they also yield this error.
    /// </summary>
    public static readonly Error NotFound = Error.NotFound("sleepdiary.entry.not_found", "Brak wpisu dla tego dnia.");

    /// <summary>
    /// The current user already has an entry for this day (<c>sleepdiary.entry.already_exists</c>, Conflict, HTTP 409).
    /// Returned when recording; to change the existing entry use the update use case instead.
    /// </summary>
    public static readonly Error AlreadyExists = Error.Conflict("sleepdiary.entry.already_exists", "Wpis dla tego dnia już istnieje.");

    /// <summary>
    /// The entry date is later than the latest allowed date (<c>sleepdiary.entry.future_date</c>, Validation, HTTP 400).
    /// The application layer allows dates up to today in UTC plus one day, to tolerate users in time zones ahead of UTC.
    /// </summary>
    public static readonly Error FutureDate = Error.Validation("sleepdiary.entry.future_date", "Nie można zapisać snu z przyszłą datą.");

    /// <summary>
    /// The wake-up time is not later than the bedtime (<c>sleepdiary.entry.wake_before_bed</c>, Validation, HTTP 400).
    /// Equal times are rejected as well.
    /// </summary>
    public static readonly Error WakeBeforeBed = Error.Validation("sleepdiary.entry.wake_before_bed", "Godzina wstania musi być późniejsza niż godzina położenia się.");

    /// <summary>
    /// The calendar day of the wake-up time differs from the entry date (<c>sleepdiary.entry.wake_date_mismatch</c>, Validation, HTTP 400).
    /// The entry date is by definition the day the user woke up.
    /// </summary>
    public static readonly Error WakeDateMismatch = Error.Validation("sleepdiary.entry.wake_date_mismatch", "Dzień wstania musi być równy dacie wpisu.");

    /// <summary>
    /// The time in bed exceeds <see cref="SleepEntry.MaxTimeInBedMinutes"/> minutes
    /// (<c>sleepdiary.entry.too_long</c>, Validation, HTTP 400).
    /// </summary>
    public static readonly Error TooLong = Error.Validation("sleepdiary.entry.too_long", "Czas w łóżku nie może przekraczać 24 godzin.");

    /// <summary>
    /// The time needed to fall asleep is negative or longer than the time in bed
    /// (<c>sleepdiary.entry.invalid_latency</c>, Validation, HTTP 400).
    /// </summary>
    public static readonly Error InvalidLatency = Error.Validation("sleepdiary.entry.invalid_latency", "Czas zasypiania musi mieścić się w czasie spędzonym w łóżku.");

    /// <summary>
    /// The number of awakenings is outside the range 0–<see cref="SleepEntry.MaxAwakenings"/>
    /// (<c>sleepdiary.entry.invalid_awakenings</c>, Validation, HTTP 400).
    /// </summary>
    public static readonly Error InvalidAwakenings =
        Error.Validation("sleepdiary.entry.invalid_awakenings", $"Liczba przebudzeń musi mieć wartość 0–{SleepEntry.MaxAwakenings}.");

    /// <summary>
    /// The note is longer than <see cref="SleepEntry.MaxNotesLength"/> characters after trimming
    /// (<c>sleepdiary.entry.notes_too_long</c>, Validation, HTTP 400).
    /// </summary>
    public static readonly Error NotesTooLong =
        Error.Validation("sleepdiary.entry.notes_too_long", $"Notatka może mieć najwyżej {SleepEntry.MaxNotesLength} znaków.");
}
