using FluentValidation;
using SleepDiary.Domain.Entries;

namespace SleepDiary.Application.Features.Entries.RecordSleepEntry;

/// <summary>
/// Input validation of <see cref="RecordSleepEntry"/>: per-field checks run before the handler, aligned with the rules of the
/// <see cref="SleepEntry"/> aggregate.
/// </summary>
/// <remarks>
/// <para>
/// Failures are returned as <c>validation.failed</c> (HTTP 400) with messages per field, so for these fields clients see this code rather than
/// the domain codes (<c>sleepdiary.entry.invalid_quality</c>, <c>sleepdiary.entry.invalid_awakenings</c>, <c>sleepdiary.entry.notes_too_long</c>).
/// Rules that depend on several fields (wake after bed, latency within the time in bed, wake day equal to the entry date) are checked only
/// by the <see cref="SleepEntry"/> aggregate.
/// </para>
/// <para>
/// The limits come from the domain constants and the note is measured after trimming, exactly as <see cref="SleepEntry.Record"/> does, so a
/// value is never accepted here and rejected by the aggregate (or the other way round) for the same field. In addition, bed and wake times
/// must be sent without <c>Z</c> or an offset (<see cref="SleepEntryInputRules.WallClockTime{T}"/>), because such values are converted
/// by the JSON serializer before they reach the command.
/// </para>
/// </remarks>
internal sealed class RecordSleepEntryValidator : AbstractValidator<RecordSleepEntry>
{
    /// <summary>
    /// Defines the per-field rules: <see cref="RecordSleepEntry.BedTime"/> and <see cref="RecordSleepEntry.WakeTime"/> are wall-clock times
    /// without a time zone, <see cref="RecordSleepEntry.Quality"/> is between <see cref="SleepQuality.Min"/> and <see cref="SleepQuality.Max"/>,
    /// <see cref="RecordSleepEntry.Awakenings"/> is between 0 and <see cref="SleepEntry.MaxAwakenings"/>,
    /// <see cref="RecordSleepEntry.SleepLatencyMinutes"/> is not negative, and the trimmed <see cref="RecordSleepEntry.Notes"/> has at most
    /// <see cref="SleepEntry.MaxNotesLength"/> characters.
    /// </summary>
    public RecordSleepEntryValidator()
    {
        RuleFor(command => command.BedTime).WallClockTime();
        RuleFor(command => command.WakeTime).WallClockTime();
        RuleFor(command => command.Quality).InclusiveBetween(SleepQuality.Min, SleepQuality.Max);
        RuleFor(command => command.Awakenings).InclusiveBetween(0, SleepEntry.MaxAwakenings);
        RuleFor(command => command.SleepLatencyMinutes).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Notes).NotesWithinLimit();
    }
}
