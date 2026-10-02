using SuperApp.Framework.Domain.Aggregates;
using Knowledge.Domain.Library.Favorites;
using Knowledge.Domain.Materials;
using Knowledge.Domain.Common;

namespace Knowledge.Domain.Library.Completions;

/// <summary>
/// Aggregate root of a completion: a user's mark that they have read, watched or listened to a material (part of the user's library, ADR-0028).
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="Favorite"/>, a minimal aggregate: created and deleted, never modified, no events. Managed with the
/// <c>knowledge.library.write</c> scope, always for the current user.
/// </para>
/// <para>
/// Rules enforced by the handler: the material must exist and be published when it is marked (<see cref="LibraryErrors.ItemNotAvailable"/>),
/// and a user marks a material at most once (the handler looks it up with <see cref="IMaterialCompletionRepository.FindAsync"/> and does nothing
/// if it exists; a unique index on (user, material) backs this up, and a concurrent duplicate that slips past the lookup fails with
/// <see cref="LibraryErrors.CompletionRecordedConcurrently"/>). Unlike favorites, completions are not removed when the material is archived.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // MarkMaterialCompletedHandler, after the availability check
/// if (await completions.FindAsync(userId, materialId, cancellationToken) is null)
/// {
///     completions.Add(MaterialCompletion.Complete(userId, materialId, clock.UtcNow));
/// }
/// </code>
/// </example>
/// <seealso cref="IMaterialCompletionRepository"/>
public sealed class MaterialCompletion : AggregateRoot<MaterialCompletionId>
{
    private MaterialCompletion(MaterialCompletionId id)
        : base(id)
    {
    }

    /// <summary>Gets the user who marked the material (the <c>sub</c> claim).</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets the material marked as completed.</summary>
    public MaterialId MaterialId { get; private set; }

    /// <summary>Gets the moment the material was marked as completed.</summary>
    public DateTimeOffset CompletedAt { get; private set; }

    /// <summary>
    /// Creates a mark that the user has completed the material. Performs no validation and cannot fail: the handler must first check that
    /// the material is published (<see cref="LibraryErrors.ItemNotAvailable"/>) and not yet marked by this user.
    /// </summary>
    /// <remarks>The caller registers the result with <see cref="IMaterialCompletionRepository.Add"/>. Raises no events.</remarks>
    /// <param name="userId">The user marking the material (the current user).</param>
    /// <param name="materialId">The completed material.</param>
    /// <param name="now">Current time, stored as <see cref="CompletedAt"/>.</param>
    /// <returns>The new completion.</returns>
    public static MaterialCompletion Complete(UserId userId, MaterialId materialId, DateTimeOffset now) =>
        new(MaterialCompletionId.New()) { UserId = userId, MaterialId = materialId, CompletedAt = now };
}
