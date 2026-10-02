namespace SuperApp.Framework.Domain.Results;

/// <summary>
/// Describes why an operation failed: a stable machine-readable <see cref="Code"/>, a human-readable <see cref="Message"/>
/// and an <see cref="ErrorType"/> that decides how the failure is reported to the caller.
/// </summary>
/// <remarks>
/// <para>
/// This code base does not use exceptions for expected failures such as invalid input, missing resources or broken business rules (ADR-0015).
/// Such failures are returned as an <see cref="Error"/> wrapped in <see cref="Result"/> / <see cref="Result{T}"/> and travel unchanged
/// from the aggregate through the handler and pipeline behaviors to the controller, which turns them into a <c>ProblemDetails</c> response
/// with the <see cref="Code"/> in the <c>code</c> extension. Exceptions remain for technical problems (database down, bugs).
/// </para>
/// <para>Conventions:</para>
/// <list type="bullet">
///   <item><description>Code format: <c>{service}.{concept}.{problem}</c> in snake_case, for example <c>knowledge.material.archived</c>.
///   Framework-level codes use a technical prefix (<c>auth.missing_scope</c>, <c>validation.failed</c>).</description></item>
///   <item><description>Codes are part of the public API contract: clients may branch on them. Never change or reuse an existing code;
///   add a new one instead. Messages may change at any time.</description></item>
///   <item><description>Define reusable errors of an aggregate as <c>static readonly</c> fields in a <c>{Aggregate}Errors</c> class
///   (for example <c>MaterialErrors.Archived</c>) so that handlers, tests and documentation reference one definition.</description></item>
///   <item><description>Messages must not contain personal data or secrets; they end up in logs and API responses.</description></item>
/// </list>
/// <para>
/// An <see cref="Error"/> converts implicitly to <see cref="Result"/> and <see cref="Result{T}"/>, so a method can simply
/// <c>return MaterialErrors.Archived;</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public static class CategoryErrors
/// {
///     public static readonly Error NotFound =
///         Error.NotFound("knowledge.category.not_found", "Category does not exist.");
///
///     public static readonly Error SlugTaken =
///         Error.Conflict("knowledge.category.slug_taken", "A category with this slug already exists.");
/// }
///
/// // in a command handler
/// if (await categories.SlugExistsAsync(command.Slug, cancellationToken))
/// {
///     return CategoryErrors.SlugTaken;
/// }
/// </code>
/// </example>
/// <seealso cref="Result"/>
/// <seealso cref="ErrorType"/>
public sealed record Error
{
    private Error(string code, string message, ErrorType type, IReadOnlyDictionary<string, string[]>? details)
    {
        Code = code;
        Message = message;
        Type = type;
        Details = details;
    }

    /// <summary>
    /// Stable, machine-readable identifier of the failure, for example <c>knowledge.material.archived</c>.
    /// </summary>
    /// <remarks>Part of the API contract (returned to clients in <c>ProblemDetails</c>); changing it is a breaking change.</remarks>
    public string Code { get; }

    /// <summary>Human-readable description of the failure.</summary>
    /// <remarks>
    /// Written to logs and returned to clients as the <c>title</c> of the <c>ProblemDetails</c> response. Clients may display it
    /// but must not parse it or branch on it; use <see cref="Code"/> for decisions.
    /// </remarks>
    public string Message { get; }

    /// <summary>Category of the failure; decides the HTTP status code of the API response.</summary>
    public ErrorType Type { get; }

    /// <summary>
    /// Field-level validation messages keyed by property name (as produced by FluentValidation), or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Filled only for <see cref="ErrorType.Validation"/> errors created by the validation pipeline behavior; returned to clients
    /// in the <c>errors</c> member of <c>ValidationProblemDetails</c>. Always <see langword="null"/> for other error types.
    /// </remarks>
    public IReadOnlyDictionary<string, string[]>? Details { get; }

    /// <summary>Creates an error for input that is malformed or out of range (HTTP 400).</summary>
    /// <param name="code">Stable error code, e.g. <c>knowledge.category.invalid_name</c>.</param>
    /// <param name="message">Description for developers; may change over time.</param>
    /// <param name="details">
    /// Optional field errors keyed by property name. Leave <see langword="null"/> in domain code; the validation behavior fills it.
    /// </param>
    /// <returns>An error of type <see cref="ErrorType.Validation"/>.</returns>
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? details = null) =>
        new(code, message, ErrorType.Validation, details);

    /// <summary>Creates an error for a caller that is not authenticated or lacks a required permission (HTTP 403).</summary>
    /// <param name="code">Stable error code, e.g. <c>auth.missing_scope</c>.</param>
    /// <param name="message">Description for developers; may change over time.</param>
    /// <returns>An error of type <see cref="ErrorType.Forbidden"/>.</returns>
    /// <remarks>
    /// Scope checks are done by the authorization pipeline behavior; use this directly only for resource-level rules
    /// (for example "only the owner may edit this entry").
    /// </remarks>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden, null);

    /// <summary>Creates an error for a resource that does not exist or must not be revealed to the caller (HTTP 404).</summary>
    /// <param name="code">Stable error code, e.g. <c>knowledge.material.not_found</c>.</param>
    /// <param name="message">Description for developers; may change over time.</param>
    /// <returns>An error of type <see cref="ErrorType.NotFound"/>.</returns>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound, null);

    /// <summary>Creates an error for a request that conflicts with existing data, typically a uniqueness rule (HTTP 409).</summary>
    /// <param name="code">Stable error code, e.g. <c>knowledge.category.slug_taken</c>.</param>
    /// <param name="message">Description for developers; may change over time.</param>
    /// <returns>An error of type <see cref="ErrorType.Conflict"/>.</returns>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict, null);

    /// <summary>Creates an error for a valid request that breaks a business rule or aggregate invariant in the current state (HTTP 422).</summary>
    /// <param name="code">Stable error code, e.g. <c>knowledge.material.content_required</c>.</param>
    /// <param name="message">Description for developers; may change over time.</param>
    /// <returns>An error of type <see cref="ErrorType.BusinessRule"/>.</returns>
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule, null);
}
