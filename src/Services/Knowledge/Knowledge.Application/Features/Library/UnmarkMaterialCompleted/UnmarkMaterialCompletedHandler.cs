using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Library.Completions;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Library.UnmarkMaterialCompleted;

/// <summary>
/// Handles <see cref="UnmarkMaterialCompleted"/>: resolves the calling user and removes that user's <see cref="MaterialCompletion"/>
/// for the material, if any.
/// </summary>
internal sealed class UnmarkMaterialCompletedHandler(IMaterialCompletionRepository completions, ICurrentUser currentUser)
    : ICommandHandler<UnmarkMaterialCompleted>
{
    /// <inheritdoc />
    public async Task<Result> Handle(UnmarkMaterialCompleted command, CancellationToken cancellationToken)
    {
        if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
        {
            return userError;
        }

        if (MaterialId.Create(command.MaterialId).TryGetValue(out var materialId, out _)
            && await completions.FindAsync(userId, materialId, cancellationToken) is { } completion)
        {
            completions.Remove(completion);
        }

        return Result.Success();
    }
}
