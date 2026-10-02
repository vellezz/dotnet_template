using SuperApp.Framework.Domain.Results;

namespace Knowledge.Domain.Common;

/// <summary>
/// Shared validation and normalization of free-text fields of the Knowledge aggregates (titles, names, descriptions).
/// </summary>
/// <remarks>
/// Both methods trim leading and trailing white space, measure the length <b>after</b> trimming and return the trimmed value,
/// so aggregates never store surrounding white space. The error code is supplied by the caller (for example
/// <c>knowledge.material.invalid_title</c>), which keeps codes specific to the aggregate and field. Used by
/// <see cref="Knowledge.Domain.Materials.Material"/>, <see cref="Knowledge.Domain.Collections.Collection"/> and
/// <see cref="Knowledge.Domain.Categories.Category"/>; block content has its own rules in
/// <see cref="Knowledge.Domain.Materials.Content.ContentBuilder"/>.
/// </remarks>
internal static class Text
{
    /// <summary>Validates a mandatory text field and returns it trimmed.</summary>
    /// <param name="value">The raw input; <see langword="null"/>, empty and white-space-only values are rejected.</param>
    /// <param name="maxLength">Maximum length in characters after trimming.</param>
    /// <param name="code">Error code returned on failure, e.g. <c>knowledge.material.invalid_title</c>.</param>
    /// <param name="field">Field name used in the error message, e.g. <c>title</c>.</param>
    /// <returns>
    /// The trimmed value, or a validation error with <paramref name="code"/> when the value is missing, blank or longer than
    /// <paramref name="maxLength"/> after trimming.
    /// </returns>
    public static Result<string> Required(string? value, int maxLength, string code, string field) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength
            ? Error.Validation(code, $"Pole '{field}' jest wymagane i może mieć najwyżej {maxLength} znaków.")
            : value.Trim();

    /// <summary>Validates an optional text field, returning it trimmed or normalized to <see langword="null"/>.</summary>
    /// <param name="value">The raw input; <see langword="null"/>, empty or white-space-only means "no value".</param>
    /// <param name="maxLength">Maximum length in characters after trimming.</param>
    /// <param name="code">Error code returned on failure, e.g. <c>knowledge.material.invalid_description</c>.</param>
    /// <param name="field">Field name used in the error message, e.g. <c>description</c>.</param>
    /// <returns>
    /// The trimmed value, <see langword="null"/> for a missing or blank value, or a validation error with <paramref name="code"/>
    /// when the trimmed value is longer than <paramref name="maxLength"/>.
    /// </returns>
    public static Result<string?> Optional(string? value, int maxLength, string code, string field) =>
        value is not null && value.Trim().Length > maxLength
            ? Error.Validation(code, $"Pole '{field}' może mieć najwyżej {maxLength} znaków.")
            : Result<string?>.Success(string.IsNullOrWhiteSpace(value) ? null : value.Trim());
}
