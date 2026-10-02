using Knowledge.Domain.Common;

namespace Knowledge.Domain.Materials;

/// <summary>
/// Write-side repository of the <see cref="Material"/> aggregate: loads materials for modification by command handlers and
/// answers the existence/status checks other use cases need before they change their own aggregate.
/// </summary>
/// <remarks>
/// Declared in the domain and implemented in <c>Knowledge.Infrastructure</c> over the write <c>DbContext</c>. Loaded aggregates are tracked:
/// changes are saved by the unit of work at the end of the command (transaction pipeline behavior), so handlers never call a save method
/// and there is no <c>Update</c>. Queries (reading materials for display) do not use this repository; they go through the read model
/// (ADR-0026).
/// </remarks>
/// <example>
/// <code>
/// var material = await materials.GetAsync(materialId, cancellationToken);
/// return material is null ? MaterialErrors.NotFound : material.Publish(clock.UtcNow);
/// </code>
/// </example>
public interface IMaterialRepository
{
    /// <summary>Loads a material with its categories and complete block content (blocks and text spans), ready to be modified.</summary>
    /// <param name="id">Identifier of the material.</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns>
    /// The tracked material in any status (including archived), or <see langword="null"/> if it does not exist; handlers then return
    /// <see cref="MaterialErrors.NotFound"/>.
    /// </returns>
    Task<Material?> GetAsync(MaterialId id, CancellationToken cancellationToken);

    /// <summary>Checks that every given material exists, in any status (used before setting the items of a collection).</summary>
    /// <param name="ids">Identifiers of the materials to check; duplicates are allowed and ignored (each distinct identifier is checked once).</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns>
    /// <see langword="true"/> if each of the given materials exists (also for an empty collection); otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> AllExistAsync(IReadOnlyCollection<MaterialId> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Checks that a material exists and has the <see cref="PublicationStatus.Published"/> status (used before adding it to favorites
    /// or marking it as completed).
    /// </summary>
    /// <param name="id">Identifier of the material.</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns><see langword="true"/> if the material is published; <see langword="false"/> if it is missing, a draft or archived.</returns>
    Task<bool> IsPublishedAsync(MaterialId id, CancellationToken cancellationToken);

    /// <summary>Registers a newly created material; it is inserted when the unit of work commits.</summary>
    /// <param name="material">The new material, as returned by <see cref="Material.Create"/>.</param>
    void Add(Material material);
}
