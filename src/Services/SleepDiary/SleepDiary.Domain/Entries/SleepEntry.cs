using SuperApp.Framework.Domain.Aggregates;
using SuperApp.Framework.Domain.Results;
using SleepDiary.Domain.Entries.Events;

namespace SleepDiary.Domain.Entries;

/// <summary>
/// Aggregate root of the SleepDiary context: one user's record of one night of sleep, identified by the owner and the day they woke up
/// (ADR-0029).
/// </summary>
/// <remarks>
/// <para>
/// A user keeps a sleep diary with at most one entry per day. The entry date is the day of waking up, so the night from Monday to Tuesday
/// is the entry for Tuesday. The entry stores when the user went to bed and got up, how long it took to fall asleep, how many times they woke up,
/// a subjective quality rating and an optional note, and derives the time in bed and the sleep time from them.
/// </para>
/// <para>Invariants, checked on <see cref="Record"/> and on every <see cref="Update"/>:</para>
/// <list type="bullet">
///   <item><description><see cref="WakeTime"/> is later than <see cref="BedTime"/> (<see cref="SleepEntryErrors.WakeBeforeBed"/>).</description></item>
///   <item><description>The calendar day of <see cref="WakeTime"/> equals <see cref="Date"/> (<see cref="SleepEntryErrors.WakeDateMismatch"/>).</description></item>
///   <item><description><see cref="TimeInBedMinutes"/> is at most <see cref="MaxTimeInBedMinutes"/> (<see cref="SleepEntryErrors.TooLong"/>).</description></item>
///   <item><description><see cref="SleepLatencyMinutes"/> is between 0 and <see cref="TimeInBedMinutes"/> (<see cref="SleepEntryErrors.InvalidLatency"/>),
///   so <see cref="SleepMinutes"/> is never negative.</description></item>
///   <item><description><see cref="Awakenings"/> is between 0 and <see cref="MaxAwakenings"/> (<see cref="SleepEntryErrors.InvalidAwakenings"/>).</description></item>
///   <item><description><see cref="Quality"/> is between <see cref="SleepQuality.Min"/> and <see cref="SleepQuality.Max"/> (<c>sleepdiary.entry.invalid_quality</c>).</description></item>
///   <item><description><see cref="Notes"/> is trimmed and at most <see cref="MaxNotesLength"/> characters long (<see cref="SleepEntryErrors.NotesTooLong"/>).</description></item>
///   <item><description><see cref="Date"/> is not later than the latest allowed date supplied by the caller (<see cref="SleepEntryErrors.FutureDate"/>);
///   checked only when the entry is recorded.</description></item>
/// </list>
/// <para>
/// Units and time: all durations are whole minutes. <see cref="BedTime"/> and <see cref="WakeTime"/> are the user's local wall-clock times
/// without a time zone (the service does not know the user's zone); they are compared as given and never converted. Only the audit timestamps
/// <see cref="CreatedAt"/> and <see cref="UpdatedAt"/> are UTC instants from the service clock.
/// </para>
/// <para>
/// Not enforced here: uniqueness of <see cref="UserId"/> + <see cref="Date"/>. The command handler checks it with
/// <see cref="ISleepEntryRepository.ExistsAsync"/> and a unique database index backs it up. <see cref="UserId"/> and <see cref="Date"/>
/// never change after creation; to move an entry to another day, delete it and record a new one.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var details = new SleepDetails(
///     new DateTime(2026, 9, 29, 23, 0, 0), new DateTime(2026, 9, 30, 7, 0, 0),
///     SleepLatencyMinutes: 15, Awakenings: 1, Quality: 4, Notes: null);
///
/// var result = SleepEntry.Record(userId, new DateOnly(2026, 9, 30), details, latestAllowedDate: new DateOnly(2026, 10, 1), now: clock.UtcNow);
/// // result.Value.TimeInBedMinutes == 480, result.Value.SleepMinutes == 465, one SleepEntryRecorded event raised
/// </code>
/// </example>
/// <seealso cref="SleepDetails"/>
/// <seealso cref="SleepEntryErrors"/>
/// <seealso cref="ISleepEntryRepository"/>
public sealed class SleepEntry : AggregateRoot<SleepEntryId>
{
    /// <summary>Largest allowed number of awakenings in one entry (inclusive).</summary>
    public const int MaxAwakenings = 50;

    /// <summary>Largest allowed length of the note, in characters, measured after trimming leading and trailing whitespace.</summary>
    public const int MaxNotesLength = 2000;

    /// <summary>Largest allowed time in bed, in minutes (24 hours, inclusive).</summary>
    public const int MaxTimeInBedMinutes = 24 * 60;

    private SleepEntry(SleepEntryId id)
        : base(id)
    {
    }

    /// <summary>Gets the owner of the entry: the CIAM <c>sub</c> of the user who recorded it. Never changes after creation.</summary>
    public UserId UserId { get; private set; }

    /// <summary>
    /// Gets the entry date: the day the user woke up, in the user's local calendar. Never changes after creation; together with
    /// <see cref="UserId"/> it uniquely identifies the entry.
    /// </summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets when the user went to bed: the user's local wall-clock date and time, without a time zone.</summary>
    public DateTime BedTime { get; private set; }

    /// <summary>
    /// Gets when the user got up: the user's local wall-clock date and time, without a time zone. Always later than <see cref="BedTime"/>
    /// and always on the day <see cref="Date"/>.
    /// </summary>
    public DateTime WakeTime { get; private set; }

    /// <summary>Gets how long it took to fall asleep, in minutes; between 0 and <see cref="TimeInBedMinutes"/>.</summary>
    public int SleepLatencyMinutes { get; private set; }

    /// <summary>Gets the number of times the user woke up during the night; between 0 and <see cref="MaxAwakenings"/>.</summary>
    public int Awakenings { get; private set; }

    /// <summary>Gets the subjective sleep quality rating given by the user.</summary>
    public SleepQuality Quality { get; private set; }

    /// <summary>Gets the note, trimmed of leading and trailing whitespace; <see langword="null"/> when the user gave no note.</summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// Gets the time in bed in whole minutes, from <see cref="BedTime"/> to <see cref="WakeTime"/> (a partial minute is truncated);
    /// at most <see cref="MaxTimeInBedMinutes"/>. Derived by the aggregate, never supplied by the caller.
    /// </summary>
    public int TimeInBedMinutes { get; private set; }

    /// <summary>
    /// Gets the sleep time in minutes: <see cref="TimeInBedMinutes"/> minus <see cref="SleepLatencyMinutes"/>; never negative.
    /// Derived by the aggregate. Awakenings are only counted; their duration is not subtracted.
    /// </summary>
    public int SleepMinutes { get; private set; }

    /// <summary>Gets the instant the entry was recorded (UTC, from the service clock <c>IClock</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the instant of the last successful change of the entry data (UTC, from the service clock); equal to <see cref="CreatedAt"/>
    /// for a new entry.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Records a new diary entry for the given user and day, validating all invariants, and raises <see cref="SleepEntryRecorded"/>
    /// carrying the entry's values and <see cref="CreatedAt"/> as its recording instant.
    /// </summary>
    /// <remarks>
    /// Does not check whether the user already has an entry for <paramref name="date"/>; the caller must do it
    /// (<see cref="ISleepEntryRepository.ExistsAsync"/>, error <see cref="SleepEntryErrors.AlreadyExists"/>). On failure no entry is created
    /// and no event is raised.
    /// </remarks>
    /// <param name="userId">Owner of the entry: the CIAM <c>sub</c> of the current user.</param>
    /// <param name="date">Entry date: the day the user woke up, in the user's local calendar.</param>
    /// <param name="details">The night's data; validated exactly as in <see cref="Update"/>.</param>
    /// <param name="latestAllowedDate">
    /// Latest date an entry may have (inclusive), computed by the caller from the clock. The service does not know the user's time zone,
    /// so the application layer passes "today in UTC plus one day" to accept users who are already in tomorrow.
    /// </param>
    /// <param name="now">Current instant (UTC); becomes <see cref="CreatedAt"/> and <see cref="UpdatedAt"/>.</param>
    /// <returns>
    /// The new entry with a new <see cref="SleepEntryId"/> and derived <see cref="TimeInBedMinutes"/> / <see cref="SleepMinutes"/>, or an error:
    /// <see cref="SleepEntryErrors.FutureDate"/> (<c>sleepdiary.entry.future_date</c>) when <paramref name="date"/> is later than
    /// <paramref name="latestAllowedDate"/>, or any validation error listed for <see cref="Update"/>.
    /// </returns>
    public static Result<SleepEntry> Record(UserId userId, DateOnly date, SleepDetails details, DateOnly latestAllowedDate, DateTimeOffset now)
    {
        if (date > latestAllowedDate)
        {
            return SleepEntryErrors.FutureDate;
        }

        var entry = new SleepEntry(SleepEntryId.New()) { UserId = userId, Date = date, CreatedAt = now };
        var applied = entry.Apply(details, now);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        entry.Raise(new SleepEntryRecorded(entry.Id, userId, date, entry.SleepMinutes, entry.Quality.Value, entry.CreatedAt));
        return entry;
    }

    /// <summary>
    /// Replaces all data of the entry with <paramref name="details"/> and recomputes the derived durations. The owner and the date stay unchanged.
    /// </summary>
    /// <remarks>
    /// This is a full replacement, not a partial patch: every field of <paramref name="details"/> is applied, and a <see langword="null"/> or blank
    /// note removes an existing note. All rules are checked before anything is changed, so a failed update leaves the entry exactly as it was.
    /// No domain event is raised (and therefore no integration event is published) for an update.
    /// </remarks>
    /// <param name="details">The new data of the night.</param>
    /// <param name="now">Current instant (UTC); becomes <see cref="UpdatedAt"/> on success.</param>
    /// <returns>
    /// Success, or the first violated rule, checked in this order:
    /// <see cref="SleepEntryErrors.WakeBeforeBed"/> (<c>sleepdiary.entry.wake_before_bed</c>),
    /// <see cref="SleepEntryErrors.WakeDateMismatch"/> (<c>sleepdiary.entry.wake_date_mismatch</c>),
    /// <see cref="SleepEntryErrors.TooLong"/> (<c>sleepdiary.entry.too_long</c>),
    /// <see cref="SleepEntryErrors.InvalidLatency"/> (<c>sleepdiary.entry.invalid_latency</c>),
    /// <see cref="SleepEntryErrors.InvalidAwakenings"/> (<c>sleepdiary.entry.invalid_awakenings</c>),
    /// the error of <see cref="SleepQuality.Create"/> (<c>sleepdiary.entry.invalid_quality</c>),
    /// <see cref="SleepEntryErrors.NotesTooLong"/> (<c>sleepdiary.entry.notes_too_long</c>). All of them are validation errors (HTTP 400).
    /// </returns>
    public Result Update(SleepDetails details, DateTimeOffset now) => Apply(details, now);

    private Result Apply(SleepDetails details, DateTimeOffset now)
    {
        if (details.WakeTime <= details.BedTime)
        {
            return SleepEntryErrors.WakeBeforeBed;
        }

        if (DateOnly.FromDateTime(details.WakeTime) != Date)
        {
            return SleepEntryErrors.WakeDateMismatch;
        }

        var timeInBed = (int)(details.WakeTime - details.BedTime).TotalMinutes;
        if (timeInBed > MaxTimeInBedMinutes)
        {
            return SleepEntryErrors.TooLong;
        }

        if (details.SleepLatencyMinutes < 0 || details.SleepLatencyMinutes > timeInBed)
        {
            return SleepEntryErrors.InvalidLatency;
        }

        if (details.Awakenings is < 0 or > MaxAwakenings)
        {
            return SleepEntryErrors.InvalidAwakenings;
        }

        if (!SleepQuality.Create(details.Quality).TryGetValue(out var quality, out var qualityError))
        {
            return qualityError;
        }

        var notes = string.IsNullOrWhiteSpace(details.Notes) ? null : details.Notes.Trim();
        if (notes is { Length: > MaxNotesLength })
        {
            return SleepEntryErrors.NotesTooLong;
        }

        BedTime = details.BedTime;
        WakeTime = details.WakeTime;
        SleepLatencyMinutes = details.SleepLatencyMinutes;
        Awakenings = details.Awakenings;
        Quality = quality;
        Notes = notes;
        TimeInBedMinutes = timeInBed;
        SleepMinutes = timeInBed - details.SleepLatencyMinutes;
        UpdatedAt = now;
        return Result.Success();
    }
}
