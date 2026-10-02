using Microsoft.AspNetCore.Diagnostics;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace SuperApp.Gateway.Hosting;

/// <summary>
/// Gives the gateway's own empty error responses (401, 403, 404, 504 of a request timeout, 502 of a failed forward) the problem body used
/// everywhere else, with <c>code</c> and <c>traceId</c> (ADR-0044), while leaving answers relayed from a BFF exactly as they came.
/// </summary>
/// <remarks>
/// <para>
/// Used as <c>app.UseStatusCodePages(GatewayStatusCodePages.WriteAsync)</c> right after <c>UseExceptionHandler</c>. Like every status code
/// page it acts only on a response with an error status and no body. The body is written through <see cref="IProblemDetailsService"/>, so
/// <c>ProblemDetailsConventions</c> adds the status-based code (<c>auth.invalid_token</c>, <c>auth.forbidden</c>, ...) and the trace id.
/// </para>
/// <para>
/// A request that reached the proxy (<see cref="IReverseProxyFeature"/> is set) carries the BFF's answer: it is never touched, because the
/// gateway must relay it unchanged (ADR-0037). The exception is a forward that failed (<see cref="IForwarderErrorFeature"/>): then the 502
/// or 504 is the gateway's own answer and gets a body. Rejections by authentication, authorization and the CSRF check happen before the
/// proxy, so they always get a body. A client that does not accept JSON (e.g. a browser navigation asking for HTML) gets the empty
/// response as before.
/// </para>
/// </remarks>
public static class GatewayStatusCodePages
{
    /// <summary>Writes the problem body for an empty error response of the gateway itself; skips answers relayed from a BFF.</summary>
    /// <param name="context">The response with an error status and no body.</param>
    /// <returns>A task that completes when the body is written or skipped.</returns>
    public static async Task WriteAsync(StatusCodeContext context)
    {
        var httpContext = context.HttpContext;
        if (httpContext.Features.Get<IReverseProxyFeature>() is not null && httpContext.Features.Get<IForwarderErrorFeature>() is null)
        {
            return;
        }

        var problems = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = httpContext.Response.StatusCode },
        });
    }
}
