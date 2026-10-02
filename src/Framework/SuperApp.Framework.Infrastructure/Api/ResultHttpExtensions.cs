using SuperApp.Framework.Domain.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SuperApp.Framework.Infrastructure.Api;

/// <summary>
/// Turns <see cref="Result"/> values returned by commands and queries into HTTP responses, in one consistent way for all services (ADR-0015).
/// </summary>
/// <remarks>
/// <para>
/// Controllers stay thin: send the request with <c>ISender</c> and return <c>this.ToActionResult(result)</c>. Never choose status codes or build
/// error bodies in a controller.
/// </para>
/// <para>Failures become <c>application/problem+json</c> (RFC 9457):</para>
/// <list type="bullet">
///   <item><description><c>status</c> from <see cref="ToStatusCode"/>; <c>title</c> = <see cref="Error.Message"/>; <c>instance</c> = request path.</description></item>
///   <item><description>extension <c>code</c> = <see cref="Error.Code"/> (what clients branch on) and <c>traceId</c> (to find the request in Tempo/Loki).</description></item>
///   <item><description>validation failures use <c>ValidationProblemDetails</c> with field messages in <c>errors</c>.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// [HttpPost("{materialId:guid}/publish")]
/// public async Task&lt;IActionResult&gt; Publish(Guid materialId, CancellationToken cancellationToken) =&gt;
///     this.ToActionResult(await sender.Send(new PublishMaterial(materialId), cancellationToken));
///
/// [HttpPost]
/// public async Task&lt;IActionResult&gt; Create(CreateCategory command, CancellationToken cancellationToken) =&gt;
///     this.ToActionResult(
///         await sender.Send(command, cancellationToken),
///         id =&gt; StatusCode(StatusCodes.Status201Created, new CreatedResponse(id)));
/// </code>
/// </example>
public static class ResultHttpExtensions
{
    /// <summary>Converts a result without a value: success becomes <c>204 No Content</c>, failure becomes a problem details response.</summary>
    /// <param name="controller">The controller handling the request (used for the response helpers and the request path).</param>
    /// <param name="result">The result of the command.</param>
    /// <returns><c>204 No Content</c>, or <c>application/problem+json</c> with the status code derived from the error type.</returns>
    public static IActionResult ToActionResult(this ControllerBase controller, Result result) =>
        result.IsSuccess ? controller.NoContent() : Problem(controller, result.Error);

    /// <summary>
    /// Converts a result with a value: success becomes <c>200 OK</c> with the value as the body (or the response built by <paramref name="onSuccess"/>),
    /// failure becomes a problem details response.
    /// </summary>
    /// <typeparam name="T">Type of the value, serialized as the response body.</typeparam>
    /// <param name="controller">The controller handling the request.</param>
    /// <param name="result">The result of the command or query.</param>
    /// <param name="onSuccess">
    /// Optional custom success response, typically <c>201 Created</c> for commands creating a resource; <see langword="null"/> returns <c>200 OK</c> with the value.
    /// </param>
    /// <returns>The success response, or <c>application/problem+json</c> with <c>code</c> and <c>traceId</c> extensions.</returns>
    public static IActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result, Func<T, IActionResult>? onSuccess = null) =>
        result.TryGetValue(out var value, out var error) ? onSuccess?.Invoke(value) ?? controller.Ok(value) : Problem(controller, error);

    /// <summary>Maps an error type to its HTTP status code: 400, 403, 404, 409 or 422 (ADR-0015).</summary>
    /// <param name="type">The error type.</param>
    /// <returns>The status code; 500 for a value that is not a defined <see cref="ErrorType"/> member, which indicates a bug.</returns>
    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static ObjectResult Problem(ControllerBase controller, Error error)
    {
        var status = error.Type.ToStatusCode();
        ProblemDetails problem = error.Details is { Count: > 0 } details
            ? new ValidationProblemDetails(details.ToDictionary(pair => pair.Key, pair => pair.Value))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = error.Message;
        problem.Instance = controller.HttpContext.Request.Path;
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = ProblemDetailsConventions.TraceId(controller.HttpContext);

        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
