namespace SleepDiary.Infrastructure.Persistence.Read.Models;

/// <summary>
/// Read model of one diary entry: a flat, primitive-typed view of the <c>sleepdiary.SleepEntries</c> table written by the aggregate (ADR-0003).
/// </summary>
/// <remarks>
/// Only the columns needed by queries are mapped (the audit timestamps and the row version are not). The values are exactly what the
/// <see cref="Domain.Entries.SleepEntry"/> aggregate stored; see its documentation for units and rules.
/// </remarks>
internal sealed class SleepEntryRow
{
    /// <summary>Gets the technical identifier of the entry.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the owner's CIAM <c>sub</c>; used only for filtering, never returned to clients.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Gets the entry date: the day the user woke up.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets the bedtime, the user's local time without a time zone.</summary>
    public DateTime BedTime { get; init; }

    /// <summary>Gets the wake-up time, the user's local time without a time zone.</summary>
    public DateTime WakeTime { get; init; }

    /// <summary>Gets the time needed to fall asleep, in minutes.</summary>
    public int SleepLatencyMinutes { get; init; }

    /// <summary>Gets the number of awakenings during the night.</summary>
    public int Awakenings { get; init; }

    /// <summary>Gets the sleep quality rating, from 1 to 5.</summary>
    public int Quality { get; init; }

    /// <summary>Gets the trimmed note, or <see langword="null"/> when there is none.</summary>
    public string? Notes { get; init; }

    /// <summary>Gets the derived time in bed, in whole minutes.</summary>
    public int TimeInBedMinutes { get; init; }

    /// <summary>Gets the derived sleep time in minutes (time in bed minus latency).</summary>
    public int SleepMinutes { get; init; }
}
