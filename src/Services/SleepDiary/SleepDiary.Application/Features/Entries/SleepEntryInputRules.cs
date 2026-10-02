using FluentValidation;
using SleepDiary.Domain.Entries;

namespace SleepDiary.Application.Features.Entries;

/// <summary>
/// FluentValidation rules shared by the validators of the commands that carry the data of one night
/// (<c>RecordSleepEntryValidator</c> and <c>UpdateSleepEntryValidator</c>).
/// </summary>
/// <remarks>
/// <para>
/// The rules mirror the domain rules of <see cref="SleepEntry"/> exactly (same limits taken from the domain constants, same trimming), so
/// input accepted here is never rejected by the aggregate for the same field and vice versa. Keep them in sync when a domain rule changes.
/// </para>
/// <para>
/// <see cref="WallClockTime{T}"/> is an input-format rule the aggregate cannot check afterwards: <c>System.Text.Json</c> converts a date-time
/// with <c>Z</c> or an offset (for example <c>+02:00</c>) to UTC or to the server's local time before the command is created, which would
/// silently shift the user's wall-clock time. Such values arrive with <see cref="DateTimeKind.Utc"/> or <see cref="DateTimeKind.Local"/>,
/// whereas a value sent without an offset has <see cref="DateTimeKind.Unspecified"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// RuleFor(command =&gt; command.BedTime).WallClockTime();
/// RuleFor(command =&gt; command.Notes).NotesWithinLimit();
/// </code>
/// </example>
internal static class SleepEntryInputRules
{
    /// <summary>
    /// Requires a wall-clock date-time sent without a time zone: the value's <see cref="DateTime.Kind"/> must be
    /// <see cref="DateTimeKind.Unspecified"/>. Values sent with <c>Z</c> or an offset are rejected instead of being converted.
    /// </summary>
    /// <typeparam name="T">Type of the validated command.</typeparam>
    /// <param name="rule">The rule builder of a <see cref="DateTime"/> property (bed time or wake time).</param>
    /// <returns>The rule builder, for chaining further rules or options.</returns>
    public static IRuleBuilderOptions<T, DateTime> WallClockTime<T>(this IRuleBuilder<T, DateTime> rule) =>
        rule.Must(value => value.Kind == DateTimeKind.Unspecified)
            .WithMessage("Czas należy podać jako lokalny czas użytkownika bez strefy czasowej (bez 'Z' i przesunięcia), np. 2026-09-29T23:15:00.");

    /// <summary>
    /// Requires the note to be at most <see cref="SleepEntry.MaxNotesLength"/> characters long after trimming leading and trailing whitespace,
    /// exactly as the aggregate measures it. <see langword="null"/>, empty and whitespace-only notes are valid (they mean "no note").
    /// </summary>
    /// <typeparam name="T">Type of the validated command.</typeparam>
    /// <param name="rule">The rule builder of the optional note property.</param>
    /// <returns>The rule builder, for chaining further rules or options.</returns>
    public static IRuleBuilderOptions<T, string?> NotesWithinLimit<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(notes => notes is null || notes.Trim().Length <= SleepEntry.MaxNotesLength)
            .WithMessage($"Notatka po usunięciu białych znaków z początku i końca może mieć najwyżej {SleepEntry.MaxNotesLength} znaków.");
}
