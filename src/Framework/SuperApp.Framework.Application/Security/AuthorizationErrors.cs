using SuperApp.Framework.Domain.Results;

namespace SuperApp.Framework.Application.Security;

/// <summary>
/// Errors returned by the authorization pipeline behavior. Their codes are part of the API contract of every service (ADR-0012, ADR-0015).
/// </summary>
/// <remarks>
/// Handlers may reuse <see cref="Unauthenticated"/> when a request needs the caller's subject but runs without one
/// (for example a "my favorites" query under the system identity).
/// </remarks>
public static class AuthorizationErrors
{
    /// <summary>
    /// The request is not marked as anonymous but the caller is not authenticated (<c>auth.unauthenticated</c>, HTTP 403); handlers return it as well when they need a user subject and the caller has none.
    /// </summary>
    /// <remarks>
    /// Requests without a token are normally rejected earlier with HTTP 401 by the API's authentication; this error covers the cases that
    /// reach the pipeline, such as a system identity executing a user-specific query.
    /// </remarks>
    public static readonly Error Unauthenticated =
        Error.Forbidden("auth.unauthenticated", "Operacja wymaga uwierzytelnionego użytkownika.");

    /// <summary>Creates the error returned when the caller's token lacks the scope required by the request (<c>auth.missing_scope</c>, HTTP 403).</summary>
    /// <param name="scope">The missing scope, in the <c>{service}.{resource}.{action}</c> convention; included in the message so the client knows what to request.</param>
    /// <returns>An error of type <see cref="ErrorType.Forbidden"/> with code <c>auth.missing_scope</c>.</returns>
    public static Error MissingScope(string scope) =>
        Error.Forbidden("auth.missing_scope", $"Brak wymaganego uprawnienia '{scope}'.");
}
