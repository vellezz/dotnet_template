using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Common;

namespace Knowledge.Application.Features.Library;

/// <summary>
/// Helpers for library use cases, which always act on the data of the calling user (fine-grained authorization in the service).
/// </summary>
/// <remarks>
/// The user is identified only by the <c>sub</c> claim of the token (<see cref="ICurrentUser.Subject"/>); the service stores no other
/// personal data (ADR-0028). Library handlers never accept a user identifier in the command, so a caller cannot read or change another
/// user's library.
/// </remarks>
internal static class CurrentUserExtensions
{
    /// <summary>Returns the <see cref="UserId"/> of the calling user, to scope a library operation to that user's own data.</summary>
    /// <param name="currentUser">The caller of the current request.</param>
    /// <returns>
    /// The user's identifier on success; <see cref="AuthorizationErrors.Unauthenticated"/> (<c>auth.unauthenticated</c>, 403) when the
    /// caller has no subject; <c>knowledge.user.invalid_id</c> (400) when the subject is longer than <see cref="UserId.MaxLength"/> characters.
    /// </returns>
    public static Result<UserId> RequireUserId(this ICurrentUser currentUser) =>
        currentUser.Subject is { } subject ? UserId.Create(subject) : AuthorizationErrors.Unauthenticated;
}
