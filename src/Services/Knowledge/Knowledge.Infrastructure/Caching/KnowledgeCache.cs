using SuperApp.Framework.Infrastructure.Caching;

namespace Knowledge.Infrastructure.Caching;

/// <summary>
/// Single place that defines the cache keys, invalidation tags and freshness settings of the Knowledge service (ADR-0020).
/// </summary>
/// <remarks>
/// <para>
/// All values are used with <see cref="FailSafeCache"/> in query handlers, and the tags are removed by the domain event handlers
/// <see cref="CategoryCacheInvalidation"/> and <see cref="MaterialCacheInvalidation"/> after a change of the data is committed. Only read-side data is
/// cached; commands never read from the cache.
/// </para>
/// <para>
/// Keys contain a version segment (<c>v1</c>): bump it whenever the shape of the cached DTO changes, otherwise replicas running the new
/// version would deserialize old JSON from Redis during a rolling update. Tags do not contain a version, so invalidation removes entries
/// of every version.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var categories = await cache.GetOrCreateAsync(
///     KnowledgeCache.CategoriesKey,
///     async token =&gt; (IReadOnlyList&lt;CategoryDto&gt;)await db.Categories.Select(...).ToListAsync(token),
///     KnowledgeCache.Categories,
///     [KnowledgeCache.CategoriesTag],
///     cancellationToken);
/// </code>
/// </example>
internal static class KnowledgeCache
{
    /// <summary>Key of the cached list of all categories (<see cref="Categories"/> settings).</summary>
    public const string CategoriesKey = "knowledge:categories:v1";

    /// <summary>
    /// Tag of the category list; removed after the commit of every command that raised a <c>CategoryChanged</c> domain event (category
    /// created or renamed).
    /// </summary>
    public const string CategoriesTag = "knowledge:categories";

    /// <summary>
    /// Settings of the category list: fresh for 5 minutes, then served stale for up to 1 hour while it is refreshed or while the database
    /// is failing. In practice invalidation by <see cref="CategoriesTag"/> makes changes visible immediately.
    /// </summary>
    public static readonly FailSafeOptions Categories = new(Fresh: TimeSpan.FromMinutes(5), MaxStale: TimeSpan.FromHours(1));

    /// <summary>
    /// Settings of the details of a published material (the reader view of <c>GetMaterial</c>): fresh for 2 minutes, served stale for up
    /// to 30 minutes. Editors bypass this cache.
    /// </summary>
    public static readonly FailSafeOptions PublishedMaterial = new(Fresh: TimeSpan.FromMinutes(2), MaxStale: TimeSpan.FromMinutes(30));

    /// <summary>Returns the cache key of the reader view of one material.</summary>
    /// <param name="materialId">Identifier of the material.</param>
    /// <returns>The key <c>knowledge:material:v1:{materialId}</c>.</returns>
    public static string MaterialKey(Guid materialId) => $"knowledge:material:v1:{materialId}";

    /// <summary>
    /// Returns the invalidation tag of one material; removed after the commit of every command that raised a <c>MaterialChanged</c> domain
    /// event of that material.
    /// </summary>
    /// <param name="materialId">Identifier of the material.</param>
    /// <returns>The tag <c>knowledge:material:{materialId}</c>.</returns>
    public static string MaterialTag(Guid materialId) => $"knowledge:material:{materialId}";
}
