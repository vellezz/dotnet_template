using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using SleepDiary.Domain.Entries;

namespace SleepDiary.Application;

/// <summary>
/// Resolves the owner of the diary for the current request. Every SleepDiary use case works only on the entries of the calling user
/// (fine-grained authorization, ADR-0029).
/// </summary>
/// <remarks>
/// The owner always comes from the access token (<see cref="ICurrentUser.Subject"/>), never from the command, so a user cannot address
/// another user's entries. The query handlers in Infrastructure apply the same rule directly on the read model.
/// </remarks>
internal static class CurrentUserExtensions
{
    /// <summary>Returns the diary owner of the current request, converted to a <see cref="UserId"/>.</summary>
    /// <param name="currentUser">The caller of the current request.</param>
    /// <returns>
    /// The <see cref="UserId"/> built from the <c>sub</c> claim; <see cref="AuthorizationErrors.Unauthenticated"/> (<c>auth.unauthenticated</c>,
    /// HTTP 403) when the caller has no subject (anonymous or system identity); or <c>sleepdiary.user.invalid_id</c> (HTTP 400) when the subject
    /// is blank or longer than <see cref="UserId.MaxLength"/> characters.
    /// </returns>
    public static Result<UserId> RequireUserId(this ICurrentUser currentUser) =>
        currentUser.Subject is { } subject ? UserId.Create(subject) : AuthorizationErrors.Unauthenticated;
}
