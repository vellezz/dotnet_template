using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using MediatR;

namespace SuperApp.Framework.Application.Messaging;

/// <summary>
/// A request to read data without changing anything. Part of the read side of CQRS.
/// </summary>
/// <remarks>
/// <para>
/// Queries do not go through the domain model: their handlers read flat read models from the <c>ReadDbContext</c> (no tracking,
/// separate connection string) and project them directly to DTOs shaped for the screen or API (ADR-0026). This keeps aggregates
/// free of reading concerns and lets reads be optimized independently (projections, caching, Dapper for heavy reports).
/// </para>
/// <para>
/// Queries pass through the same pipeline as commands except the transaction behavior: logging, authorization
/// (<see cref="RequiresScopeAttribute"/>) and validation (for example page size limits).
/// </para>
/// <para>Conventions:</para>
/// <list type="bullet">
///   <item><description>Place the query, its DTOs and its validator in <c>Application/Features/{Aggregate}/{UseCase}/</c>; name it after what it returns
///   (<c>GetMaterial</c>, <c>ListCollections</c>).</description></item>
///   <item><description>Its handler lives in the Infrastructure project under the same folder structure, because it depends on EF Core
///   (<c>Infrastructure/Features/{Aggregate}/{UseCase}Handler.cs</c>).</description></item>
///   <item><description>Return <see cref="Result{T}"/> with a DTO, a list of DTOs or <see cref="PagedResult{T}"/>; return a
///   <see cref="ErrorType.NotFound"/> error when a single requested item does not exist.</description></item>
///   <item><description>DTOs contain only primitives, never domain types.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// [RequiresScope(KnowledgeScopes.LibraryRead)]
/// public sealed record ListMyFavorites(int Page = 1, int PageSize = 20) : IQuery&lt;Result&lt;PagedResult&lt;FavoriteDto&gt;&gt;&gt;;
/// </code>
/// </example>
/// <typeparam name="TResponse">Result type of the query, always <see cref="Result{T}"/> wrapping the returned read model.</typeparam>
/// <seealso cref="IQueryHandler{TQuery, TResponse}"/>
/// <seealso cref="ICommand{TResponse}"/>
public interface IQuery<TResponse> : IRequest<TResponse>
    where TResponse : IResultFactory<TResponse>;
