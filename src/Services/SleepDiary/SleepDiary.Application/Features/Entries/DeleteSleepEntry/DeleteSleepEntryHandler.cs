using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using SleepDiary.Domain.Entries;

namespace SleepDiary.Application.Features.Entries.DeleteSleepEntry;

/// <summary>
/// Handles <see cref="DeleteSleepEntry"/>: loads the current user's entry for the day and marks it for deletion.
/// </summary>
/// <remarks>
/// The deletion is executed by the transaction pipeline behavior, which saves the unit of work only when this handler returns success.
/// </remarks>
/// <param name="entries">Write-side repository of diary entries.</param>
/// <param name="currentUser">The caller; the owner of the entry is always taken from it.</param>
internal sealed class DeleteSleepEntryHandler(ISleepEntryRepository entries, ICurrentUser currentUser) : ICommandHandler<DeleteSleepEntry>
{
    /// <inheritdoc />
    public async Task<Result> Handle(DeleteSleepEntry command, CancellationToken cancellationToken)
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

        entries.Remove(entry);
        return Result.Success();
    }
}
