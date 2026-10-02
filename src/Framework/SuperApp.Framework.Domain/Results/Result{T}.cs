using System.Diagnostics.CodeAnalysis;

namespace SuperApp.Framework.Domain.Results;

/// <summary>
/// Outcome of an operation that either succeeds with a value of type <typeparamref name="T"/> or fails with an <see cref="Result.Error"/>.
/// </summary>
/// <remarks>
/// <para>
/// Used by factories (<c>Category.Create</c> returns <c>Result&lt;Category&gt;</c>), by value object creation
/// (<c>SleepQuality.Create</c> returns <c>Result&lt;SleepQuality&gt;</c>), by commands that return an identifier
/// (<c>ICommand&lt;Result&lt;Guid&gt;&gt;</c>) and by all queries (<c>IQuery&lt;Result&lt;MaterialDetailsDto&gt;&gt;</c>).
/// </para>
/// <para>
/// Both a value and an <see cref="Error"/> convert implicitly to <see cref="Result{T}"/>, so a method returning
/// <c>Result&lt;Category&gt;</c> can simply <c>return category;</c> or <c>return CategoryErrors.InvalidSlug;</c>.
/// </para>
/// <para>
/// The value is read only through <see cref="TryGetValue"/> (or transformed with <see cref="Map{TOut}"/>): it checks the outcome and gives
/// the value (or the error) a name in one step, so reading a value that is not there cannot even be written (ADR-0047). There is no
/// <c>Value</c> property: for value types (identifiers, value objects, <see cref="Guid"/>) a nullable property could not be checked by the
/// compiler and would silently yield <see langword="default"/>, and a throwing one would move the check to run time. Tests unwrap results
/// with <c>ResultAssert</c> from <c>SuperApp.Framework.Testing</c>.
/// </para>
/// <para>
/// Because <see cref="Result{T}"/> derives from <see cref="Result"/>, code that only cares about success or failure
/// (pipeline behaviors, logging) can treat every result uniformly.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public static Result&lt;SleepQuality&gt; Create(int value) =&gt;
///     value is &lt; Min or &gt; Max
///         ? Error.Validation("sleepdiary.entry.invalid_quality", "Quality must be between 1 and 5.")
///         : new SleepQuality(value);
///
/// // caller
/// if (!SleepQuality.Create(command.Quality).TryGetValue(out var quality, out var error))
/// {
///     return error;
/// }
///
/// // quality is guaranteed to be valid here and can be passed to the aggregate
/// </code>
/// </example>
/// <typeparam name="T">Type of the value produced on success.</typeparam>
/// <seealso cref="Result"/>
public sealed class Result<T> : Result, IResultFactory<Result<T>>
{
    private readonly T? _value;

    private Result(T value)
        : base(null) => _value = value;

    private Result(Error error)
        : base(error)
    {
    }

    /// <summary>
    /// Returns the value of a successful result or the error of a failed one, so that the caller can branch and name the outcome in a single
    /// expression.
    /// </summary>
    /// <remarks>
    /// This is the recommended way to consume a <see cref="Result{T}"/> in handlers and aggregates: after the check, the variable holds the
    /// aggregate, value object or identifier itself, not the result wrapping it. Discard the error with <c>out _</c> when the caller
    /// replaces it with its own (for example an invalid identifier reported as "not found").
    /// </remarks>
    /// <example>
    /// <code>
    /// if (!Category.Create(command.Name, command.Slug).TryGetValue(out var category, out var error))
    /// {
    ///     return error;
    /// }
    ///
    /// categories.Add(category);
    /// return category.Id.Value;
    /// </code>
    /// </example>
    /// <param name="value">The value when the result is successful; <see langword="default"/> otherwise.</param>
    /// <param name="error">The error when the result is a failure; <see langword="null"/> otherwise.</param>
    /// <returns><see langword="true"/> for a successful result, <see langword="false"/> for a failure.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value, [NotNullWhen(false)] out Error? error)
    {
        if (IsSuccess)
        {
            value = _value!;
            error = null;
            return true;
        }

        value = default;
        error = Error;
        return false;
    }

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <param name="value">The value produced by the operation.</param>
    /// <returns>A successful result. Returning <paramref name="value"/> directly has the same effect thanks to the implicit conversion.</returns>
    public static Result<T> Success(T value) => new(value);

    /// <summary>Creates a failed result of this value type.</summary>
    /// <param name="error">The reason of the failure.</param>
    /// <returns>A failed result. Returning <paramref name="error"/> directly has the same effect thanks to the implicit conversion.</returns>
    public static new Result<T> Failure(Error error) => new(error);

    static Result<T> IResultFactory<Result<T>>.FromError(Error error) => Failure(error);

    /// <summary>Transforms the value of a successful result, keeping a failure unchanged.</summary>
    /// <typeparam name="TOut">Type of the transformed value.</typeparam>
    /// <param name="map">Transformation applied to the value; not called when the result is a failure.</param>
    /// <returns>A successful result with the transformed value, or a failed result with the original <see cref="Result.Error"/>.</returns>
    /// <example>
    /// <code>
    /// Result&lt;Guid&gt; id = Category.Create(name, slug).Map(category =&gt; category.Id.Value);
    /// </code>
    /// </example>
    public Result<TOut> Map<TOut>(Func<T, TOut> map) => IsSuccess ? Result<TOut>.Success(map(_value!)) : Result<TOut>.Failure(Error);

    /// <summary>Converts a value into a successful result, so that methods can <c>return value;</c>.</summary>
    /// <param name="value">The value produced by the operation.</param>
    /// <returns>A successful result.</returns>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Converts an <see cref="Results.Error"/> into a failed result, so that methods can <c>return SomeErrors.X;</c>.</summary>
    /// <param name="error">The reason of the failure.</param>
    /// <returns>A failed result.</returns>
    public static implicit operator Result<T>(Error error) => Failure(error);
}
