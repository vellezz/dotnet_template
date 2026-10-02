using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Domain.ValueObjects;

namespace Knowledge.Domain.Common;

/// <summary>
/// Absolute HTTPS address of an external resource, such as the main video or podcast file of a material.
/// The Knowledge service stores media only as links, never as files, and accepts only <c>https</c> (ADR-0028).
/// </summary>
/// <remarks>
/// <para>
/// A single-value object (ADR-0024). A valid value is an absolute URI with the <c>https</c> scheme and a length between 1 and
/// <see cref="MaxLength"/> characters; <c>http</c>, <c>javascript:</c>, <c>data:</c> and relative addresses are rejected, which protects
/// clients from mixed content and script injection. The value is stored exactly as given (not normalized).
/// </para>
/// <para>
/// Aggregates use <see cref="WebUrl"/> for typed properties (<see cref="Knowledge.Domain.Materials.Material.MainMediaUrl"/>).
/// Block content keeps URLs as plain strings in <see cref="Knowledge.Domain.Materials.Content.ContentBlock"/> but validates them with
/// the same rule through <see cref="IsHttps(string?)"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var url = WebUrl.Create("https://cdn.example.com/episode-12.mp3");
/// if (url.IsFailure)
/// {
///     return url.Error; // knowledge.url.invalid
/// }
/// </code>
/// </example>
public readonly record struct WebUrl : ISingleValueObject<WebUrl, string>
{
    /// <summary>Maximum length of the address in characters.</summary>
    public const int MaxLength = 2048;

    private WebUrl(string value) => Value = value;

    /// <inheritdoc />
    public string Value { get; }

    /// <summary>Creates the address from untrusted input, accepting only valid absolute HTTPS URLs.</summary>
    /// <param name="value">The raw address; see <see cref="IsHttps(string?)"/> for the rules.</param>
    /// <returns>The address, or a validation error <c>knowledge.url.invalid</c> when <paramref name="value"/> is not a valid HTTPS URL.</returns>
    public static Result<WebUrl> Create(string value) =>
        IsHttps(value)
            ? new WebUrl(value)
            : Error.Validation("knowledge.url.invalid", $"Adres '{value}' musi być poprawnym adresem https.");

    /// <inheritdoc />
    public static WebUrl FromTrusted(string value) => new(value);

    /// <summary>Checks whether a text is an absolute URL with the <c>https</c> scheme and at most <see cref="MaxLength"/> characters.</summary>
    /// <remarks>Use it where the URL stays a plain string (block content, links in text spans) but must follow the same rule as <see cref="WebUrl"/>.</remarks>
    /// <param name="value">The address to check; may be <see langword="null"/> (which is not valid).</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid HTTPS address; otherwise <see langword="false"/>.</returns>
    public static bool IsHttps(string? value) =>
        value is { Length: > 0 and <= MaxLength }
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>Returns the raw address, so that string interpolation shows the URL instead of the type name.</summary>
    /// <returns>The wrapped address.</returns>
    public override string ToString() => Value;
}
