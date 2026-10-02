using SuperApp.Framework.Domain.Results;

namespace SuperApp.Framework.Domain.ValueObjects;

/// <summary>
/// Contract for a value object that wraps exactly one primitive value and guarantees it is valid,
/// for example <c>SleepQuality</c> (an <see cref="int"/> between 1 and 5) or <c>WebUrl</c> (an absolute HTTPS URL).
/// </summary>
/// <remarks>
/// <para>
/// A single-value object replaces "primitive obsession": instead of passing an <see cref="int"/> and validating it in several places,
/// the domain passes a <c>SleepQuality</c> that cannot hold an invalid value (ADR-0024). Validation lives in exactly one place, the
/// <see cref="Create(TValue)"/> factory.
/// </para>
/// <para>How to write one:</para>
/// <list type="bullet">
///   <item><description>Declare a <c>readonly record struct</c> in the <c>Domain</c> project with a private constructor and a get-only <c>Value</c> property.</description></item>
///   <item><description>Implement <see cref="Create(TValue)"/> with the validation rules and an error code of your context.</description></item>
///   <item><description>Implement <see cref="FromTrusted(TValue)"/> as a plain constructor call without validation.</description></item>
///   <item><description>Override <see cref="object.ToString"/> to return the value, so that logs and string interpolation show the value instead of the type name.</description></item>
///   <item><description>Never create it with <c>default</c> or <c>new()</c>: that would bypass validation. Analyzer APP002 reports it as a compile error.</description></item>
/// </list>
/// <para>
/// No mapping code is needed: <c>SuperApp.Framework.Infrastructure</c> discovers all implementations and provides the EF Core value conversion
/// (stored as <typeparamref name="TValue"/>, loaded with <see cref="FromTrusted(TValue)"/>), the JSON converter (read through
/// <see cref="Create(TValue)"/>, so invalid JSON input is rejected with HTTP 400) and the OpenAPI schema (a plain primitive).
/// Keep value objects inside the domain and the application layer; integration events (<c>Contracts</c>) and read models use primitives.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public readonly record struct SleepQuality : ISingleValueObject&lt;SleepQuality, int&gt;
/// {
///     public const int Min = 1;
///     public const int Max = 5;
///
///     private SleepQuality(int value) =&gt; Value = value;
///
///     public int Value { get; }
///
///     public static Result&lt;SleepQuality&gt; Create(int value) =&gt;
///         value is &lt; Min or &gt; Max
///             ? Error.Validation("sleepdiary.entry.invalid_quality", "Quality must be between 1 and 5.")
///             : new SleepQuality(value);
///
///     public static SleepQuality FromTrusted(int value) =&gt; new(value);
///
///     public override string ToString() =&gt; Value.ToString(CultureInfo.InvariantCulture);
/// }
/// </code>
/// </example>
/// <typeparam name="TSelf">The implementing value object type itself (a <c>readonly record struct</c>).</typeparam>
/// <typeparam name="TValue">The primitive type that is wrapped, stored in the database and serialized, e.g. <see cref="int"/>, <see cref="string"/>, <see cref="Guid"/>.</typeparam>
/// <seealso cref="IStronglyTypedId{TSelf, TValue}"/>
public interface ISingleValueObject<TSelf, TValue>
    where TSelf : struct, ISingleValueObject<TSelf, TValue>
    where TValue : notnull
{
    /// <summary>Gets the wrapped primitive value; it always satisfies the rules of the type.</summary>
    TValue Value { get; }

    /// <summary>
    /// Creates the value object from untrusted input (user input, command data), applying all validation rules of the type.
    /// This is the only way domain and application code should create it.
    /// </summary>
    /// <param name="value">The raw value to validate and wrap.</param>
    /// <returns>
    /// The value object, or a failed result with an <see cref="ErrorType.Validation"/> error whose code identifies the rule
    /// (e.g. <c>sleepdiary.entry.invalid_quality</c>).
    /// </returns>
    static abstract Result<TSelf> Create(TValue value);

    /// <summary>
    /// Recreates the value object from a value that was already validated, without running validation again.
    /// </summary>
    /// <remarks>
    /// Reserved for infrastructure (EF Core materialization of values read from the database) and for tests building fixtures.
    /// Do not call it on user input; use <see cref="Create(TValue)"/> instead.
    /// </remarks>
    /// <param name="value">A value previously produced by <see cref="Create(TValue)"/> and persisted.</param>
    /// <returns>The value object wrapping <paramref name="value"/>.</returns>
    static abstract TSelf FromTrusted(TValue value);
}
