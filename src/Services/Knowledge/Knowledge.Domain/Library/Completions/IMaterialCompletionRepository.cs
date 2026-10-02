using Knowledge.Domain.Materials;
using Knowledge.Domain.Common;

namespace Knowledge.Domain.Library.Completions;

/// <summary>
/// Write-side repository of the <see cref="MaterialCompletion"/> aggregate: finds, adds and removes a user's "completed" marks.
/// </summary>
/// <remarks>
/// Implemented in <c>Knowledge.Infrastructure</c>. Changes take effect when the unit of work commits. Listing a user's completed materials
/// for display goes through the read side.
/// </remarks>
public interface IMaterialCompletionRepository
{
    /// <summary>Finds the mark of a material as completed by a given user.</summary>
    /// <param name="userId">The user (the current user).</param>
    /// <param name="materialId">The material.</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns>The tracked completion, or <see langword="null"/> if the user has not marked the material as completed.</returns>
    Task<MaterialCompletion?> FindAsync(UserId userId, MaterialId materialId, CancellationToken cancellationToken);

    /// <summary>Registers a new completion; it is inserted when the unit of work commits.</summary>
    /// <param name="completion">The new completion, as returned by <see cref="MaterialCompletion.Complete"/>.</param>
    void Add(MaterialCompletion completion);

    /// <summary>Marks a completion for deletion (the user un-marks the material); it is deleted when the unit of work commits.</summary>
    /// <param name="completion">The completion to delete, previously loaded with <see cref="FindAsync"/>.</param>
    void Remove(MaterialCompletion completion);
}
