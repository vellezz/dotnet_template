using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Library.Completions;
using Knowledge.Domain.Library;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Library.MarkMaterialCompleted;

/// <summary>
/// Handles <see cref="MarkMaterialCompleted"/>: resolves the calling user, checks that the material is published and records a
/// <see cref="MaterialCompletion"/> if the user has none for it yet.
/// </summary>
/// <remarks>
/// Only the publication status of the material is read; the material aggregate is not loaded. Uniqueness per user and material is
/// checked here and enforced by a unique index. When two concurrent requests both pass the check, the second save hits the index and the
/// unit of work returns <see cref="LibraryErrors.CompletionRecordedConcurrently"/> (409) instead of the idempotent success of a
/// sequential duplicate.
/// </remarks>
internal sealed class MarkMaterialCompletedHandler(
    IMaterialCompletionRepository completions,
    IMaterialRepository materials,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<MarkMaterialCompleted>
{
    /// <inheritdoc />
    public async Task<Result> Handle(MarkMaterialCompleted command, CancellationToken cancellationToken)
    {
        if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
        {
            return userError;
        }

        if (!MaterialId.Create(command.MaterialId).TryGetValue(out var materialId, out _)
            || !await materials.IsPublishedAsync(materialId, cancellationToken))
        {
            return LibraryErrors.ItemNotAvailable;
        }

        if (await completions.FindAsync(userId, materialId, cancellationToken) is null)
        {
            completions.Add(MaterialCompletion.Complete(userId, materialId, clock.UtcNow));
        }

        return Result.Success();
    }
}
