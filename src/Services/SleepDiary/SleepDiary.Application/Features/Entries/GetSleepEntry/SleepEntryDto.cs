namespace SleepDiary.Application.Features.Entries.GetSleepEntry;

/// <summary>
/// One sleep diary entry as returned by the API, by <see cref="GetSleepEntry"/> and inside <c>SleepDiaryDto</c>.
/// </summary>
/// <remarks>
/// A flat read model with primitives only, projected from the database without loading the aggregate (ADR-0026). Times are the user's
/// local wall-clock times without a time zone (ADR-0029): they are returned exactly as the user entered them and must not be converted.
/// All durations are whole minutes.
/// </remarks>
/// <param name="Id">Technical identifier of the entry (GUID). Entries are addressed by <paramref name="Date"/> in the API.</param>
/// <param name="Date">Entry date: the day the user woke up.</param>
/// <param name="BedTime">When the user went to bed, local time without a time zone (usually on the day before <paramref name="Date"/>).</param>
/// <param name="WakeTime">When the user got up, local time without a time zone; always later than <paramref name="BedTime"/> and on the day <paramref name="Date"/>.</param>
/// <param name="SleepLatencyMinutes">How long it took to fall asleep, in minutes; between 0 and <paramref name="TimeInBedMinutes"/>.</param>
/// <param name="Awakenings">Number of awakenings during the night, from 0 to 50.</param>
/// <param name="Quality">Subjective sleep quality, from 1 (worst) to 5 (best).</param>
/// <param name="Notes">The user's note, trimmed; <see langword="null"/> when there is none.</param>
/// <param name="TimeInBedMinutes">Derived time in bed in whole minutes, from <paramref name="BedTime"/> to <paramref name="WakeTime"/>; at most 1440.</param>
/// <param name="SleepMinutes">Derived sleep time in minutes: <paramref name="TimeInBedMinutes"/> minus <paramref name="SleepLatencyMinutes"/>.</param>
public sealed record SleepEntryDto(
    Guid Id,
    DateOnly Date,
    DateTime BedTime,
    DateTime WakeTime,
    int SleepLatencyMinutes,
    int Awakenings,
    int Quality,
    string? Notes,
    int TimeInBedMinutes,
    int SleepMinutes);
