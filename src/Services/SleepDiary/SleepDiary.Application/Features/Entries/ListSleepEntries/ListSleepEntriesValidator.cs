using FluentValidation;

namespace SleepDiary.Application.Features.Entries.ListSleepEntries;

/// <summary>
/// Validates the date range of <see cref="ListSleepEntries"/> before the query runs: both ends must be given, and the range must be ordered
/// and bounded to keep the result size bounded.
/// </summary>
/// <remarks>
/// <para>
/// Run by the validation pipeline behavior (after authorization); failures are returned as <c>validation.failed</c> (HTTP 400) with
/// messages per field.
/// </para>
/// <para>
/// A missing <c>from</c> or <c>to</c> query parameter is bound by ASP.NET Core as <c>default(DateOnly)</c> (<c>0001-01-01</c>), because the
/// parameters are non-nullable. That value is therefore treated as "not given" and rejected; the range rules are checked only when both ends
/// are given, so a missing end produces a single, clear message.
/// </para>
/// </remarks>
internal sealed class ListSleepEntriesValidator : AbstractValidator<ListSleepEntries>
{
    /// <summary>Largest number of days a range may cover, counting both ends (a leap year).</summary>
    public const int MaxRangeDays = 366;

    /// <summary>
    /// Defines the rules: <see cref="ListSleepEntries.From"/> and <see cref="ListSleepEntries.To"/> are given (not <c>0001-01-01</c>),
    /// <c>To</c> is not before <c>From</c>, and the inclusive range has at most <see cref="MaxRangeDays"/> days.
    /// </summary>
    public ListSleepEntriesValidator()
    {
        RuleFor(query => query.From).NotEqual(default(DateOnly)).WithMessage("Parametr 'from' jest wymagany.");
        RuleFor(query => query.To).NotEqual(default(DateOnly)).WithMessage("Parametr 'to' jest wymagany.");

        When(query => query.From != default && query.To != default, () =>
        {
            RuleFor(query => query.To).GreaterThanOrEqualTo(query => query.From);
            RuleFor(query => query).Must(query => query.To.DayNumber - query.From.DayNumber < MaxRangeDays)
                .WithMessage($"Zakres dat może obejmować najwyżej {MaxRangeDays} dni.");
        });
    }
}
