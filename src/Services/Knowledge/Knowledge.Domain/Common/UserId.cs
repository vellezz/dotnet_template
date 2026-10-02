using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Domain.ValueObjects;

namespace Knowledge.Domain.Common;

/// <summary>
/// Identifier of an end user as issued by the CIAM: the value of the <c>sub</c> claim of the access token.
/// It is the only piece of user data the Knowledge service stores (ADR-0028).
/// </summary>
/// <remarks>
/// <para>
/// A single-value object (ADR-0024), not a strongly typed GUID ID: the format of <c>sub</c> belongs to the CIAM and is treated as an
/// opaque string. The value is stored as given (not trimmed, case-sensitive). It owns the user's library entries
/// (<see cref="Knowledge.Domain.Library.Favorites.Favorite"/>, <see cref="Knowledge.Domain.Library.Completions.MaterialCompletion"/>).
/// </para>
/// <para>
/// Application code obtains it from the current user (<c>ICurrentUser</c>), never from request bodies, so that a user cannot act on
/// behalf of another one. Do not log it together with other data that could identify a person.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // RequireUserId returns Result&lt;UserId&gt; built from the "sub" claim
/// if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
/// {
///     return userError;
/// }
///
/// favorites.Add(Favorite.Add(userId, command.ItemType, command.ItemId, clock.UtcNow));
/// </code>
/// </example>
public readonly record struct UserId : ISingleValueObject<UserId, string>
{
    /// <summary>Maximum length of the identifier in characters.</summary>
    public const int MaxLength = 200;

    private UserId(string value) => Value = value;

    /// <inheritdoc />
    public string Value { get; }

    /// <summary>Creates the identifier from an untrusted value, typically the <c>sub</c> claim.</summary>
    /// <param name="value">The raw identifier; must not be <see langword="null"/>, empty or white space, and at most <see cref="MaxLength"/> characters.</param>
    /// <returns>The identifier, or a validation error <c>knowledge.user.invalid_id</c> when the value is blank or too long.</returns>
    public static Result<UserId> Create(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > MaxLength
            ? Error.Validation("knowledge.user.invalid_id", "Niepoprawny identyfikator użytkownika.")
            : new UserId(value);

    /// <inheritdoc />
    public static UserId FromTrusted(string value) => new(value);

    /// <summary>Returns the raw identifier, so that string interpolation shows the value instead of the type name.</summary>
    /// <returns>The wrapped <c>sub</c> value.</returns>
    public override string ToString() => Value;
}
