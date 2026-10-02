namespace SuperApp.Gateway.Bff.Security;

/// <summary>
/// CSRF protection of the <c>bff-web</c> profile: requests to <c>/api/*</c> must carry the header <c>X-CSRF: 1</c>, otherwise they are
/// answered with 401 before routing and authentication run (ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// The session cookie is sent automatically by the browser, so the gateway must be sure a request comes from our own SPA. A custom header
/// cannot be added by a plain HTML form or link from another site, and a cross-origin script would need a CORS preflight that the gateway
/// does not allow. Together with <c>SameSite=Strict</c> on the cookie this blocks cross-site request forgery.
/// </para>
/// <para>
/// The Angular application adds the header in an HTTP interceptor. The check applies to every HTTP method, including GET.
/// The <c>/bff/*</c> endpoints are not covered: <c>/bff/login</c> and <c>/bff/logout</c> are top-level navigations that cannot send a custom
/// header (<c>/bff/logout</c> is protected by its <c>sid</c> parameter instead, see <see cref="SuperApp.Gateway.Bff.BffEndpoints"/>), <c>/bff/user</c>
/// only reads data, and <c>/bff/backchannel-logout</c> is called by the CIAM. Registered only in the <c>bff-web</c> profile;
/// mobile clients send a bearer token, which a browser never attaches on its own.
/// </para>
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
internal sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Rejects a protected request without the <c>X-CSRF: 1</c> header with 401 and code <c>auth.csrf_header_missing</c>; passes every
    /// other request on.
    /// </summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>A task that completes when the request has been rejected or processed by the rest of the pipeline.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) && context.Request.Headers["X-CSRF"] != "1")
        {
            return RejectAsync(context);
        }

        return next(context);
    }

    // 401 with code auth.csrf_header_missing, so the SPA can tell a missing interceptor from an expired session (auth.invalid_token).
    private static async Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Missing X-CSRF header.",
            Extensions = { ["code"] = "auth.csrf_header_missing" },
        };
        await context.RequestServices.GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
