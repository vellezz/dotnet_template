using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace SuperApp.Framework.Infrastructure.Security;

/// <summary>
/// Audit log of the internal API: records which client (another experience's BFF) called which internal endpoint (ADR-0039, ADR-0040).
/// </summary>
/// <remarks>
/// <para>
/// Registered by every BFF as a global MVC filter (<c>options.Filters.Add&lt;InternalApiCallAudit&gt;()</c> in <c>Program.cs</c>); it logs
/// only requests under <c>/internal</c>, after authorization has let them through, so every successful entry into the internal API leaves
/// one entry (event ID 230). The caller is the <c>azp</c> claim of the
/// user's token (the client the token was issued to, e.g. <c>dashboard-bff</c>), so the operators can see which experiences depend on this
/// one before changing or retiring an internal version.
/// </para>
/// <para>
/// The endpoint is logged as its route template, never as the actual path, and nothing about the user is logged (ADR-0008).
/// </para>
/// </remarks>
/// <param name="logger">Logger of the audit entries.</param>
public sealed partial class InternalApiCallAudit(ILogger<InternalApiCallAudit> logger) : IActionFilter
{
    /// <summary>Logs the call when the request targets the internal API.</summary>
    /// <param name="context">The action about to run, with its request and route.</param>
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.Request.Path.StartsWithSegments("/internal", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LogInternalApiCalled(
            logger,
            context.ActionDescriptor.AttributeRouteInfo?.Template ?? context.ActionDescriptor.DisplayName ?? "unknown",
            context.HttpContext.User.FindFirst("azp")?.Value ?? "unknown");
    }

    /// <summary>Does nothing; the audit entry is written before the action.</summary>
    /// <param name="context">The executed action.</param>
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    // Source-generated log method (the only allowed way of logging, see architecture rules §11).
    [LoggerMessage(230, LogLevel.Information, "Internal API {Endpoint} called by client {CallerClientId}")]
    private static partial void LogInternalApiCalled(ILogger logger, string endpoint, string callerClientId);
}
