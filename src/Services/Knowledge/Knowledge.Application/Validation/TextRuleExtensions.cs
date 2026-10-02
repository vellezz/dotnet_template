using FluentValidation;

namespace Knowledge.Application.Validation;

/// <summary>
/// FluentValidation rules for free-text fields that measure the text the way the Knowledge aggregates store it: trimmed.
/// </summary>
/// <remarks>
/// <para>
/// The aggregates (<c>Material</c>, <c>Collection</c>, <c>Category</c>) trim leading and trailing white space and check the length of the
/// trimmed value against their own constants (for example <see cref="Knowledge.Domain.Materials.Material.MaxTitleLength"/>). A validator
/// that measured the raw input would reject a value that fits after trimming (<c>"  Title  "</c> with a title of exactly the maximum
/// length) and would disagree with the domain. Validators therefore use these rules with the domain constants, never their own numbers.
/// </para>
/// <para>
/// Blank values are handled by the usual <c>NotEmpty()</c>, which already rejects <see langword="null"/>, empty and white-space-only
/// strings, exactly like the aggregates do for required fields. A <see langword="null"/> value passes the length rule, so optional fields
/// only need <see cref="MaximumTrimmedLength{T}"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// RuleFor(command =&gt; command.Title).NotEmpty().MaximumTrimmedLength(Material.MaxTitleLength);
/// RuleFor(command =&gt; command.Description).MaximumTrimmedLength(Material.MaxDescriptionLength);
/// </code>
/// </example>
internal static class TextRuleExtensions
{
    /// <summary>
    /// Requires the value, after trimming leading and trailing white space, to have at most <paramref name="maxLength"/> characters.
    /// </summary>
    /// <typeparam name="T">Type of the validated command.</typeparam>
    /// <param name="rule">The rule builder of a string property.</param>
    /// <param name="maxLength">Maximum length in characters after trimming; pass the constant of the aggregate that stores the value.</param>
    /// <returns>The rule builder, for chaining further rules or options.</returns>
    public static IRuleBuilderOptions<T, string?> MaximumTrimmedLength<T>(this IRuleBuilder<T, string?> rule, int maxLength) =>
        rule.Must(value => value is null || value.Trim().Length <= maxLength)
            .WithMessage($"Pole '{{PropertyName}}' może mieć najwyżej {maxLength} znaków (bez białych znaków na początku i końcu).");
}
