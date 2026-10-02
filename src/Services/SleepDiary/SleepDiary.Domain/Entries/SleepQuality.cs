using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Domain.ValueObjects;

namespace SleepDiary.Domain.Entries;

/// <summary>
/// Value object: the user's subjective rating of a night's sleep on a scale from <see cref="Min"/> (worst) to <see cref="Max"/> (best).
/// </summary>
/// <remarks>
/// <para>
/// A single-value object (ADR-0024): immutable, created only through <see cref="Create"/>, which guarantees the value is in range.
/// It is stored as an <see cref="int"/> column by the <c>SuperApp.Framework</c> EF convention (with a database <c>CHECK</c> constraint on the same range)
/// and exposed to clients, DTOs and integration events as a plain <see cref="int"/>.
/// </para>
/// <para>Never use <c>default(SleepQuality)</c>: its value 0 is outside the scale (analyzer APP002 reports it).</para>
/// </remarks>
/// <seealso cref="SleepEntry.Quality"/>
public readonly record struct SleepQuality : ISingleValueObject<SleepQuality, int>
{
    /// <summary>Lowest (worst) rating on the scale, inclusive.</summary>
    public const int Min = 1;

    /// <summary>Highest (best) rating on the scale, inclusive.</summary>
    public const int Max = 5;

    private SleepQuality(int value) => Value = value;

    /// <summary>Gets the rating, between <see cref="Min"/> and <see cref="Max"/>.</summary>
    public int Value { get; }

    /// <summary>Creates a quality rating from the value given by the user.</summary>
    /// <param name="value">The rating; must be between <see cref="Min"/> and <see cref="Max"/> inclusive.</param>
    /// <returns>
    /// The rating, or a validation error <c>sleepdiary.entry.invalid_quality</c> (HTTP 400) when <paramref name="value"/>
    /// is outside the range <see cref="Min"/>–<see cref="Max"/>.
    /// </returns>
    public static Result<SleepQuality> Create(int value) =>
        value is < Min or > Max
            ? Error.Validation("sleepdiary.entry.invalid_quality", $"Jakość snu musi mieć wartość {Min}–{Max}.")
            : new SleepQuality(value);

    /// <summary>Wraps a value without validation. Use only for values that are already valid, for example ones read from the database.</summary>
    /// <param name="value">A trusted rating between <see cref="Min"/> and <see cref="Max"/>.</param>
    /// <returns>The quality rating.</returns>
    public static SleepQuality FromTrusted(int value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
