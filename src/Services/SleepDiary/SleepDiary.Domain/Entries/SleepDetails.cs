namespace SleepDiary.Domain.Entries;

/// <summary>
/// The user-supplied data of one night in the sleep diary, passed to <see cref="SleepEntry.Record"/> and <see cref="SleepEntry.Update"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a plain input carrier, not a value object: it holds the raw values from the command and performs no validation.
/// All rules (wake after bed, at most <see cref="SleepEntry.MaxTimeInBedMinutes"/> minutes in bed, latency within the time in bed,
/// ranges of awakenings and quality, notes length) are checked by the <see cref="SleepEntry"/> aggregate, which rejects invalid data
/// with a <see cref="SleepEntryErrors"/> error.
/// </para>
/// <para>
/// Times are wall-clock times in the user's local time zone, without an offset (ADR-0029). The service does not know the user's time zone,
/// so the values are compared as given and never converted; their <see cref="DateTime.Kind"/> is ignored here. The application layer
/// rejects input that carried <c>Z</c> or an offset (a <see cref="DateTime.Kind"/> other than <see cref="DateTimeKind.Unspecified"/>)
/// before it reaches the aggregate, because the JSON serializer would already have converted such a value.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var details = new SleepDetails(
///     BedTime: new DateTime(2026, 9, 29, 23, 0, 0),
///     WakeTime: new DateTime(2026, 9, 30, 7, 0, 0),
///     SleepLatencyMinutes: 15,
///     Awakenings: 1,
///     Quality: 4,
///     Notes: null);
/// var entry = SleepEntry.Record(userId, new DateOnly(2026, 9, 30), details, latestAllowedDate, clock.UtcNow);
/// </code>
/// </example>
/// <param name="BedTime">When the user went to bed: local date and time of the user, without a time zone. Usually on the day before the entry date.</param>
/// <param name="WakeTime">
/// When the user got up: local date and time of the user, without a time zone. Must be later than <paramref name="BedTime"/>, at most
/// <see cref="SleepEntry.MaxTimeInBedMinutes"/> minutes after it, and its calendar day must equal the entry date.
/// </param>
/// <param name="SleepLatencyMinutes">How long it took to fall asleep, in minutes; from 0 up to the time spent in bed.</param>
/// <param name="Awakenings">Number of times the user woke up during the night; from 0 to <see cref="SleepEntry.MaxAwakenings"/>.</param>
/// <param name="Quality">Subjective sleep quality rating, from <see cref="SleepQuality.Min"/> (worst) to <see cref="SleepQuality.Max"/> (best).</param>
/// <param name="Notes">
/// Optional free-text note. It is trimmed and may then have at most <see cref="SleepEntry.MaxNotesLength"/> characters;
/// <see langword="null"/>, empty or whitespace-only text means "no note" (and removes an existing note on update).
/// </param>
/// <seealso cref="SleepEntry"/>
public sealed record SleepDetails(
    DateTime BedTime,
    DateTime WakeTime,
    int SleepLatencyMinutes,
    int Awakenings,
    int Quality,
    string? Notes);
