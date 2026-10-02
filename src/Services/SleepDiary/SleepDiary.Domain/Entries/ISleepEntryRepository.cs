namespace SleepDiary.Domain.Entries;

/// <summary>
/// Write-side repository of the <see cref="SleepEntry"/> aggregate: loads entries for modification and registers new or deleted ones
/// in the current unit of work.
/// </summary>
/// <remarks>
/// <para>
/// A diary entry is addressed by its natural key, the pair owner (<see cref="UserId"/>) + entry date (the day the user woke up),
/// which is unique within the service (ADR-0029, enforced by a unique database index on <c>UserId</c> + <c>Date</c>).
/// The technical <see cref="SleepEntryId"/> is not used for lookups: clients always work with "my entry for this day".
/// </para>
/// <para>
/// Methods only stage changes in the EF Core change tracker. Nothing is written until the transaction pipeline behavior calls
/// <c>IUnitOfWork.SaveChangesAsync</c> after a successful command; command handlers must never save on their own.
/// Read-side queries do not use this repository; they read the <c>SleepDiaryReadDbContext</c> directly (ADR-0026).
/// </para>
/// <para>The implementation is <c>SleepEntryRepository</c> in <c>SleepDiary.Infrastructure</c>.</para>
/// </remarks>
/// <example>
/// <code>
/// var entry = await entries.FindAsync(userId, command.Date, cancellationToken);
/// if (entry is null)
/// {
///     return SleepEntryErrors.NotFound;
/// }
///
/// entries.Remove(entry);
/// return Result.Success();
/// </code>
/// </example>
/// <seealso cref="SleepEntry"/>
public interface ISleepEntryRepository
{
    /// <summary>Loads the user's diary entry for the given day, tracked for modification.</summary>
    /// <param name="userId">Owner of the entry (the CIAM <c>sub</c> of the current user).</param>
    /// <param name="date">Entry date: the day the user woke up, in the user's local calendar.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>
    /// The tracked entry, or <see langword="null"/> when the user has no entry for that day. Changes made to the returned aggregate
    /// are persisted by the unit of work without any further call.
    /// </returns>
    Task<SleepEntry?> FindAsync(UserId userId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Checks whether the user already has a diary entry for the given day.</summary>
    /// <remarks>
    /// Used to reject a duplicate before creating a new entry. The check and the later insert are not atomic; two concurrent requests
    /// for the same day are ultimately stopped by the unique database index.
    /// </remarks>
    /// <param name="userId">Owner of the entry (the CIAM <c>sub</c> of the current user).</param>
    /// <param name="date">Entry date: the day the user woke up, in the user's local calendar.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns><see langword="true"/> if an entry for this user and day exists; otherwise <see langword="false"/>.</returns>
    Task<bool> ExistsAsync(UserId userId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Registers a new entry in the unit of work; it is inserted when the command's changes are saved.</summary>
    /// <param name="entry">A new entry created by <see cref="SleepEntry.Record"/>; its domain events are dispatched on save.</param>
    void Add(SleepEntry entry);

    /// <summary>Marks an entry for deletion in the unit of work; it is deleted when the command's changes are saved.</summary>
    /// <param name="entry">An entry loaded by <see cref="FindAsync"/> in the same unit of work.</param>
    void Remove(SleepEntry entry);
}
