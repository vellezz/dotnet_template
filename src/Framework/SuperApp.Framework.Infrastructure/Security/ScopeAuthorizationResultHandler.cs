using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SuperApp.Framework.Infrastructure.Security;

/// <summary>
/// Answers a request rejected by an authorization policy of the host (for example the scope policy of a BFF's internal API) with the same
/// problem response the domain services return: <c>403</c> with <c>code</c> <c>auth.missing_scope</c> and <c>traceId</c> (ADR-0015).
/// </summary>
/// <remarks>
/// <para>
/// Domain services check scopes in the MediatR pipeline and answer <c>auth.missing_scope</c> themselves. A host without the pipeline, such as a
/// BFF, checks them with ASP.NET Core authorization policies (<see cref="ScopePolicyExtensions.RequireScope"/>), whose default 403 has no
/// body; clients that branch on <c>code</c> would not recognize it. This handler writes the problem body for a forbidden result and leaves
/// every other outcome (success, challenge = 401) to the default handler.
/// </para>
/// <para>Register it in the host: <c>builder.Services.AddSingleton&lt;IAuthorizationMiddlewareResultHandler, ScopeAuthorizationResultHandler&gt;();</c></para>
/// </remarks>
public sealed class ScopeAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <summary>Writes a <c>403 auth.missing_scope</c> problem for a forbidden result; delegates everything else to the default handler.</summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="context">The current request.</param>
    /// <param name="policy">The evaluated policy.</param>
    /// <param name="authorizeResult">The result of the evaluation.</param>
    /// <returns>A task that completes when the response is written or the pipeline continues.</returns>
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Brak wymaganego uprawnienia.",
            Instance = context.Request.Path,
        };
        problem.Extensions["code"] = "auth.missing_scope";
        problem.Extensions["traceId"] = SuperApp.Framework.Infrastructure.Api.ProblemDetailsConventions.TraceId(context);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}
