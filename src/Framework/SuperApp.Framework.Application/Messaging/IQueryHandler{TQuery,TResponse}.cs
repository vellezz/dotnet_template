using SuperApp.Framework.Domain.Results;
using MediatR;

namespace SuperApp.Framework.Application.Messaging;

/// <summary>
/// Handles one query type by reading read models from the database and projecting them to the DTO declared by the query.
/// </summary>
/// <remarks>
/// <para>
/// Implementations live in the Infrastructure project (<c>Infrastructure/Features/{Aggregate}/{UseCase}Handler.cs</c>), are
/// <c>internal sealed</c>, and use the service's <c>ReadDbContext</c> directly, without repositories or aggregates (ADR-0026).
/// Project with <c>Select</c> so that only the needed columns are read; the read context has change tracking disabled.
/// </para>
/// <para>
/// Handlers may cache results with <c>FailSafeCache</c> (ADR-0020); the cache is invalidated by tags from domain event handlers
/// when the underlying data changes. Never cache on the write side.
/// </para>
/// <para>
/// Handlers are discovered automatically when the Infrastructure assembly is passed to <c>AddAppApplication</c>.
/// Test them with integration tests against a real SQL Server (Testcontainers), not with mocks.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// internal sealed class ListCategoriesHandler(KnowledgeReadDbContext db, FailSafeCache cache)
///     : IQueryHandler&lt;ListCategories, Result&lt;IReadOnlyList&lt;CategoryDto&gt;&gt;&gt;
/// {
///     public async Task&lt;Result&lt;IReadOnlyList&lt;CategoryDto&gt;&gt;&gt; Handle(ListCategories query, CancellationToken cancellationToken)
///     {
///         var categories = await cache.GetOrCreateAsync(
///             KnowledgeCache.CategoriesKey,
///             async token =&gt; (IReadOnlyList&lt;CategoryDto&gt;)await db.Categories
///                 .OrderBy(category =&gt; category.Name)
///                 .Select(category =&gt; new CategoryDto(category.Id, category.Name, category.Slug))
///                 .ToListAsync(token),
///             KnowledgeCache.Categories,
///             [KnowledgeCache.CategoriesTag],
///             cancellationToken);
///
///         return Result&lt;IReadOnlyList&lt;CategoryDto&gt;&gt;.Success(categories);
///     }
/// }
/// </code>
/// </example>
/// <typeparam name="TQuery">The query type handled by this class.</typeparam>
/// <typeparam name="TResponse">The result type declared by the query, a <see cref="Result{T}"/> with the read model.</typeparam>
/// <seealso cref="IQuery{TResponse}"/>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
    where TResponse : IResultFactory<TResponse>;
