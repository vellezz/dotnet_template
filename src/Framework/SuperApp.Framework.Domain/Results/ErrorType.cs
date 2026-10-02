namespace SuperApp.Framework.Domain.Results;

/// <summary>
/// Category of an <see cref="Error"/>. It tells callers what kind of failure happened and determines the HTTP status code
/// returned by the API (ADR-0015).
/// </summary>
/// <remarks>
/// Pick the category from the caller's point of view: "what should the client do about it?". The mapping to HTTP is done in one place
/// (<c>ResultHttpExtensions</c> in <c>SuperApp.Framework.Infrastructure</c>), so controllers never choose status codes themselves.
/// </remarks>
public enum ErrorType
{
    /// <summary>
    /// The input is malformed or out of range (empty name, too long text, invalid identifier). The client must fix the request.
    /// Returned as HTTP 400 with field errors in <see cref="Error.Details"/> when available.
    /// </summary>
    Validation,

    /// <summary>
    /// The caller is not authenticated or lacks the required scope (see <c>RequiresScopeAttribute</c>). Returned as HTTP 403.
    /// </summary>
    Forbidden,

    /// <summary>
    /// The addressed resource does not exist, or the caller is not allowed to know that it exists (for example a draft visible only to editors).
    /// Returned as HTTP 404.
    /// </summary>
    NotFound,

    /// <summary>
    /// The request collides with the current state of other data, typically a uniqueness rule (a slug that is already taken,
    /// a favorite that already exists). Returned as HTTP 409.
    /// </summary>
    Conflict,

    /// <summary>
    /// The request is well formed but violates a business rule or an aggregate invariant in the current state
    /// (publishing an archived material, publishing without content). Returned as HTTP 422.
    /// </summary>
    BusinessRule,
}
