using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Domain.ValueObjects;

namespace SleepDiary.Domain.Entries;

/// <summary>
/// Value object: identifier of the diary owner, the <c>sub</c> claim issued by the CIAM. It is the only piece of user data the service stores.
/// </summary>
/// <remarks>
/// <para>
/// The service keeps no user profile: entries belong to a user solely through this opaque string, taken from the access token of the
/// current request (<c>ICurrentUser.Subject</c>), never from the request body. Do not parse it or assume a format (GUID, e-mail);
/// compare it only for equality.
/// </para>
/// <para>
/// A single-value object (ADR-0024) stored as an <c>nvarchar(<see cref="MaxLength"/>)</c> column. Never use <c>default(UserId)</c>:
/// it wraps <see langword="null"/> (analyzer APP002 reports it).
/// </para>
/// </remarks>
/// <seealso cref="SleepEntry.UserId"/>
public readonly record struct UserId : ISingleValueObject<UserId, string>
{
    /// <summary>Largest allowed length of the identifier, in characters; also the length of the database column.</summary>
    public const int MaxLength = 200;

    private UserId(string value) => Value = value;

    /// <summary>Gets the value of the <c>sub</c> claim; not blank and at most <see cref="MaxLength"/> characters long.</summary>
    public string Value { get; }

    /// <summary>Creates the identifier from the value of the <c>sub</c> claim, with validation.</summary>
    /// <param name="value">The <c>sub</c> claim of the current user.</param>
    /// <returns>
    /// The identifier, or a validation error <c>sleepdiary.user.invalid_id</c> (HTTP 400) when <paramref name="value"/> is empty,
    /// whitespace only or longer than <see cref="MaxLength"/> characters.
    /// </returns>
    public static Result<UserId> Create(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > MaxLength
            ? Error.Validation("sleepdiary.user.invalid_id", "Niepoprawny identyfikator użytkownika.")
            : new UserId(value);

    /// <summary>Wraps a value without validation. Use only for values that are already valid, for example ones read from the database.</summary>
    /// <param name="value">A trusted <c>sub</c> value.</param>
    /// <returns>The user identifier.</returns>
    public static UserId FromTrusted(string value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
