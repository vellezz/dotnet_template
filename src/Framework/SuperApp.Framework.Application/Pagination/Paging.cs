namespace SuperApp.Framework.Application.Pagination;

/// <summary>
/// Shared paging limits and offset calculation for list queries returning <see cref="PagedResult{T}"/>.
/// </summary>
/// <remarks>
/// Every list endpoint of every service uses the same limit, so clients can rely on one rule and the database is protected
/// from unbounded reads.
/// </remarks>
public static class Paging
{
    /// <summary>
    /// The largest page size any list query returns (100). Handlers clamp the requested page size to <c>1..MaxPageSize</c>.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>Computes how many items to skip to reach the start of a page.</summary>
    /// <param name="page">Page number starting at 1; values below 1 are treated as the first page, so a bad request never produces a negative offset.</param>
    /// <param name="pageSize">Effective page size; clamp it first, this method does not validate it.</param>
    /// <returns>The number of items before the requested page, to pass to <c>Skip</c>: <c>(page - 1) * pageSize</c>.</returns>
    public static int Skip(int page, int pageSize) => (Math.Max(page, 1) - 1) * pageSize;
}
