using FluentValidation;
using SleepDiary.Domain.Entries;

namespace SleepDiary.Application.Features.Entries.UpdateSleepEntry;

/// <summary>
/// Input validation of <see cref="UpdateSleepEntry"/>: the same per-field checks as for recording, run before the handler and aligned with
/// the rules of <see cref="SleepEntry.Update"/>.
/// </summary>
/// <remarks>
/// Failures are returned as <c>validation.failed</c> (HTTP 400) with messages per field. Cross-field rules are checked only by
/// <see cref="SleepEntry.Update"/>. The limits come from the domain constants and the note is measured after trimming, as in the aggregate;
/// bed and wake times must be sent without <c>Z</c> or an offset (see <see cref="SleepEntryInputRules"/> and <c>RecordSleepEntryValidator</c>).
/// </remarks>
internal sealed class UpdateSleepEntryValidator : AbstractValidator<UpdateSleepEntry>
{
    /// <summary>
    /// Defines the per-field rules: <see cref="UpdateSleepEntry.BedTime"/> and <see cref="UpdateSleepEntry.WakeTime"/> are wall-clock times
    /// without a time zone, <see cref="UpdateSleepEntry.Quality"/> is between <see cref="SleepQuality.Min"/> and <see cref="SleepQuality.Max"/>,
    /// <see cref="UpdateSleepEntry.Awakenings"/> is between 0 and <see cref="SleepEntry.MaxAwakenings"/>,
    /// <see cref="UpdateSleepEntry.SleepLatencyMinutes"/> is not negative, and the trimmed <see cref="UpdateSleepEntry.Notes"/> has at most
    /// <see cref="SleepEntry.MaxNotesLength"/> characters.
    /// </summary>
    public UpdateSleepEntryValidator()
    {
        RuleFor(command => command.BedTime).WallClockTime();
        RuleFor(command => command.WakeTime).WallClockTime();
        RuleFor(command => command.Quality).InclusiveBetween(SleepQuality.Min, SleepQuality.Max);
        RuleFor(command => command.Awakenings).InclusiveBetween(0, SleepEntry.MaxAwakenings);
        RuleFor(command => command.SleepLatencyMinutes).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Notes).NotesWithinLimit();
    }
}
