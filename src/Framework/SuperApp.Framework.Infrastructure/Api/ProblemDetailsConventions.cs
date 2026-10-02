using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SuperApp.Framework.Infrastructure.Api;

/// <summary>
/// Makes every error response of an API look the same: <c>application/problem+json</c> with a stable <c>code</c> and a <c>traceId</c> in one
/// format, also for errors produced by ASP.NET Core itself (ADR-0015, ADR-0044).
/// </summary>
/// <remarks>
/// <para>
/// Business and validation errors already carry their own code (<see cref="ResultHttpExtensions"/>). Responses produced by the framework do
/// not: model binding errors (400), a missing or invalid token (401), an unknown path (404), an unhandled exception (500). This convention,
/// registered as <c>ProblemDetailsOptions.CustomizeProblemDetails</c> by <c>AddAppServiceDefaults</c>, adds to every such problem:
/// </para>
/// <list type="bullet">
///   <item><description><c>code</c> derived from the status when the problem has none (<see cref="CodeFor"/>); an existing code is never
///   replaced;</description></item>
///   <item><description><c>traceId</c> as the 32-character W3C trace identifier (<see cref="TraceId"/>), replacing the default
///   <c>00-…-…-01</c> form, so every response can be found in Tempo and Loki the same way;</description></item>
///   <item><description><c>instance</c> = the request path, when missing.</description></item>
/// </list>
/// <para>
/// It applies to problems created through ASP.NET Core (<c>ProblemDetailsFactory</c>, <c>IProblemDetailsService</c>): automatic 400 of
/// <c>[ApiController]</c>, <c>UseStatusCodePages</c> for empty 401/403/404 responses and <c>UseExceptionHandler</c>. Problems written by
/// the framework itself (<see cref="ResultHttpExtensions"/>, <see cref="DownstreamUnavailableExceptionHandler"/>,
/// <c>ScopeAuthorizationResultHandler</c>) set the same members directly with <see cref="TraceId"/>.
/// </para>
/// </remarks>
public static class ProblemDetailsConventions
{
    /// <summary>Adds <c>code</c>, <c>traceId</c> and <c>instance</c> to a problem created by ASP.NET Core.</summary>
    /// <param name="context">The problem being written and its request.</param>
    public static void Apply(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;

        problem.Extensions["traceId"] = TraceId(context.HttpContext);
        if (!problem.Extensions.ContainsKey("code"))
        {
            problem.Extensions["code"] = CodeFor(status);
        }

        problem.Instance ??= context.HttpContext.Request.Path;
    }

    /// <summary>Returns the trace identifier written to every problem response: the W3C trace id (32 hex characters).</summary>
    /// <param name="httpContext">The current request; its <c>TraceIdentifier</c> is the fallback when there is no activity.</param>
    /// <returns>The trace id of the current activity, or the request's trace identifier without an activity.</returns>
    public static string TraceId(HttpContext httpContext) =>
        Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

    /// <summary>Returns the code of a problem that ASP.NET Core created without one, by HTTP status.</summary>
    /// <param name="status">The HTTP status of the response.</param>
    /// <returns>
    /// <c>request.malformed</c> (400: the request cannot be read or bound, e.g. broken JSON, a number in quotes, an unknown enum value in
    /// the query; a client bug, unlike <c>validation.failed</c> of the validators, which reports wrong data entered by the user),
    /// <c>auth.invalid_token</c> (401), <c>auth.forbidden</c> (403), <c>http.not_found</c> (404), <c>http.method_not_allowed</c> (405),
    /// <c>http.unsupported_media_type</c> (415), <c>http.too_many_requests</c> (429), <c>server.error</c> (5xx), otherwise <c>http.{status}</c>.
    /// </returns>
    public static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "request.malformed",
        StatusCodes.Status401Unauthorized => "auth.invalid_token",
        StatusCodes.Status403Forbidden => "auth.forbidden",
        StatusCodes.Status404NotFound => "http.not_found",
        StatusCodes.Status405MethodNotAllowed => "http.method_not_allowed",
        StatusCodes.Status415UnsupportedMediaType => "http.unsupported_media_type",
        StatusCodes.Status429TooManyRequests => "http.too_many_requests",
        >= 500 => "server.error",
        _ => $"http.{status}",
    };
}
