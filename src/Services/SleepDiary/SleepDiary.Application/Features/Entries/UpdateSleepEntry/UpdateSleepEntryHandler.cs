using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using SleepDiary.Domain.Entries;

namespace SleepDiary.Application.Features.Entries.UpdateSleepEntry;

/// <summary>
/// Handles <see cref="UpdateSleepEntry"/>: loads the current user's entry for the day and applies the new data through <see cref="SleepEntry.Update"/>.
/// </summary>
/// <remarks>
/// The changes are saved by the transaction pipeline behavior only when the aggregate accepts them. The entry has a row version, so a concurrent
/// modification of the same entry fails with a concurrency exception on save instead of silently overwriting it.
/// </remarks>
/// <param name="entries">Write-side repository of diary entries.</param>
/// <param name="currentUser">The caller; the owner of the entry is always taken from it.</param>
/// <param name="clock">Source of the current UTC time for <see cref="SleepEntry.UpdatedAt"/>.</param>
internal sealed class UpdateSleepEntryHandler(ISleepEntryRepository entries, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<UpdateSleepEntry>
{
    /// <inheritdoc />
    public async Task<Result> Handle(UpdateSleepEntry command, CancellationToken cancellationToken)
    {
        if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
        {
            return userError;
        }

        var entry = await entries.FindAsync(userId, command.Date, cancellationToken);
        if (entry is null)
        {
            return SleepEntryErrors.NotFound;
        }

        var details = new SleepDetails(command.BedTime, command.WakeTime, command.SleepLatencyMinutes, command.Awakenings, command.Quality, command.Notes);
        return entry.Update(details, clock.UtcNow);
    }
}
