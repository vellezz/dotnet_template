using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Refit;

namespace SuperApp.Framework.Infrastructure.Api;

/// <summary>
/// Turns the answer of another API of the system (Refit <see cref="IApiResponse"/>) into the response of a BFF action, relaying the callee's
/// status and error body unchanged (ADR-0015, ADR-0038).
/// </summary>
/// <remarks>
/// <para>
/// A BFF does not reinterpret errors of the domain services: the service decided the status and the stable <c>code</c> (e.g.
/// <c>404 knowledge.material.not_found</c>), and the module branches on that code. So an error answer is relayed as is: same status, same
/// <c>application/problem+json</c> body (including <c>code</c> and <c>traceId</c>). An answer without a body keeps only the status.
/// </para>
/// <para>
/// Success is relayed with the callee's status (200, 201 with its body, 204), or replaced by the caller's own response, for example when the
/// BFF reshapes the data. Transport failures (callee unreachable, timeout) are not answers; they surface as exceptions handled by
/// <see cref="DownstreamUnavailableExceptionHandler"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpGet("{materialId:guid}")]
/// public async Task&lt;IActionResult&gt; Get(Guid materialId, CancellationToken cancellationToken) =&gt;
///     this.ToActionResult(await knowledge.MaterialsGetAsync(materialId, cancellationToken));
/// </code>
/// </example>
public static class DownstreamResponseExtensions
{
    /// <summary>Relays an answer without a typed body (commands answering 204, or an error).</summary>
    /// <param name="controller">The BFF controller.</param>
    /// <param name="response">The callee's answer.</param>
    /// <returns>The callee's success status, or its error status with the unchanged error body.</returns>
    public static IActionResult ToActionResult(this ControllerBase controller, IApiResponse response) =>
        response.IsSuccessful ? controller.StatusCode(StatusOf(response)) : Relay(response);

    /// <summary>Relays an answer with a typed body.</summary>
    /// <typeparam name="T">The body type generated from the callee's contract.</typeparam>
    /// <param name="controller">The BFF controller.</param>
    /// <param name="response">The callee's answer.</param>
    /// <param name="onSuccess">
    /// Optional own response built from the body; <see langword="null"/> relays the body with the callee's status.
    /// </param>
    /// <returns>The success response, or the callee's error status with the unchanged error body.</returns>
    public static IActionResult ToActionResult<T>(this ControllerBase controller, IApiResponse<T> response, Func<T, IActionResult>? onSuccess = null)
    {
        if (!response.IsSuccessful)
        {
            return Relay(response);
        }

        if (response.Content is null)
        {
            return controller.StatusCode(StatusOf(response));
        }

        return onSuccess?.Invoke(response.Content) ?? new ObjectResult(response.Content) { StatusCode = StatusOf(response) };
    }

    private static IActionResult Relay(IApiResponse response)
    {
        var status = StatusOf(response);
        var body = (response.Error as ApiException)?.Content;
        if (string.IsNullOrEmpty(body))
        {
            return new StatusCodeResult(status);
        }

        return new ContentResult
        {
            StatusCode = status,
            Content = body,
            ContentType = response.ContentHeaders?.ContentType?.ToString() ?? "application/problem+json",
        };
    }

    // No status code means the request never got an answer (transport failure): rethrow, so that DownstreamUnavailableExceptionHandler
    // answers 503/504 instead of relaying a status that does not exist.
    private static int StatusOf(IApiResponse response) =>
        response.StatusCode is { } status
            ? (int)status
            : throw (Exception?)response.Error ?? new HttpRequestException("Downstream call returned no response.");
}
