namespace Knowledge.Domain.Categories;

/// <summary>
/// Write-side repository of the <see cref="Category"/> aggregate: loads categories for modification and answers the uniqueness and
/// existence checks the aggregate cannot do itself.
/// </summary>
/// <remarks>
/// Implemented in <c>Knowledge.Infrastructure</c> over the write <c>DbContext</c>. Changes to loaded categories and added categories are
/// saved by the unit of work at the end of the command; there is no save or update method. Category lists for display come from the
/// read side, not from here.
/// </remarks>
public interface ICategoryRepository
{
    /// <summary>Loads a category for modification (the loaded instance is tracked by the unit of work).</summary>
    /// <param name="id">Identifier of the category.</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns>The category, or <see langword="null"/> if it does not exist (handlers then return <see cref="CategoryErrors.NotFound"/>).</returns>
    Task<Category?> GetAsync(CategoryId id, CancellationToken cancellationToken);

    /// <summary>Checks whether a slug is already used by an existing category.</summary>
    /// <remarks>
    /// A check-then-insert is not atomic: two concurrent requests may both pass it. The unique index on the slug then rejects the second
    /// insert, and <c>IUnitOfWork.SaveChangesAsync</c> maps that violation to the same <see cref="CategoryErrors.SlugTaken"/>, so the caller
    /// sees one error code for both the sequential and the concurrent duplicate.
    /// </remarks>
    /// <param name="slug">The slug to look up, compared as stored (slugs are lowercase by construction).</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns><see langword="true"/> if a category with this slug exists; otherwise <see langword="false"/>.</returns>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Checks that every given category exists (before assigning categories to a material or collection).</summary>
    /// <param name="ids">Identifiers of the categories; duplicates are allowed and ignored (each distinct identifier is checked once).</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns>
    /// <see langword="true"/> if each of the given categories exists (also for an empty collection); otherwise <see langword="false"/>,
    /// which handlers report as <see cref="CategoryErrors.UnknownCategories"/>.
    /// </returns>
    Task<bool> AllExistAsync(IReadOnlyCollection<CategoryId> ids, CancellationToken cancellationToken);

    /// <summary>Registers a newly created category; it is inserted when the unit of work commits.</summary>
    /// <param name="category">The new category, as returned by <see cref="Category.Create"/>.</param>
    void Add(Category category);
}
