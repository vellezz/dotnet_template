using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using System.Collections.Concurrent;
using System.Reflection;
using MediatR;

namespace SuperApp.Framework.Application.Behaviors;

/// <summary>
/// Second pipeline behavior: enforces the coarse-grained authorization declared on the request type with
/// <see cref="RequiresScopeAttribute"/> and <see cref="AllowAnonymousRequestAttribute"/> (ADR-0012, ADR-0017).
/// </summary>
/// <remarks>
/// Rules that depend on the state of a resource (ownership, drafts visible only to editors) are not checked here; they belong to
/// the handler or the aggregate. Attribute lookups are cached per request type.
/// </remarks>
internal sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResultFactory<TResponse>
{
    private static readonly ConcurrentDictionary<Type, (bool Anonymous, string? Scope)> Requirements = new();

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var (anonymous, scope) = Requirements.GetOrAdd(typeof(TRequest), static type => (
            type.GetCustomAttribute<AllowAnonymousRequestAttribute>() is not null,
            type.GetCustomAttribute<RequiresScopeAttribute>()?.Scope));

        if (anonymous)
        {
            return next();
        }

        if (!currentUser.IsAuthenticated)
        {
            return Task.FromResult(TResponse.FromError(AuthorizationErrors.Unauthenticated));
        }

        if (scope is not null && !currentUser.HasScope(scope))
        {
            return Task.FromResult(TResponse.FromError(AuthorizationErrors.MissingScope(scope)));
        }

        return next();
    }
}
