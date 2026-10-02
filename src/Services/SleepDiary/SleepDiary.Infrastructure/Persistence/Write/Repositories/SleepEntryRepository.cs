using Microsoft.EntityFrameworkCore;
using SleepDiary.Domain.Entries;

namespace SleepDiary.Infrastructure.Persistence.Write.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISleepEntryRepository"/> on <see cref="SleepDiaryWriteDbContext"/>.
/// </summary>
/// <remarks>
/// Lookups use the unique index on <c>UserId</c> + <c>Date</c>. <see cref="Add"/> and <see cref="Remove"/> only change the change tracker;
/// the transaction pipeline behavior saves the unit of work.
/// </remarks>
/// <param name="context">The scoped write context shared with the rest of the command's unit of work.</param>
internal sealed class SleepEntryRepository(SleepDiaryWriteDbContext context) : ISleepEntryRepository
{
    /// <inheritdoc />
    public Task<SleepEntry?> FindAsync(UserId userId, DateOnly date, CancellationToken cancellationToken) =>
        context.Set<SleepEntry>().FirstOrDefaultAsync(entry => entry.UserId == userId && entry.Date == date, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(UserId userId, DateOnly date, CancellationToken cancellationToken) =>
        context.Set<SleepEntry>().AnyAsync(entry => entry.UserId == userId && entry.Date == date, cancellationToken);

    /// <inheritdoc />
    public void Add(SleepEntry entry) => context.Add(entry);

    /// <inheritdoc />
    public void Remove(SleepEntry entry) => context.Remove(entry);
}
