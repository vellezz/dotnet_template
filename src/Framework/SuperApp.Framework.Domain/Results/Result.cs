using System.Diagnostics.CodeAnalysis;

namespace SuperApp.Framework.Domain.Results;

/// <summary>
/// Outcome of an operation that either succeeds without a value or fails with an <see cref="Results.Error"/>.
/// It is the standard return type for aggregate methods and commands that change state but produce nothing (ADR-0015).
/// </summary>
/// <remarks>
/// <para>
/// Instead of throwing on expected failures, methods return a <see cref="Result"/> and the caller decides what to do.
/// This makes every failure path visible in the signature and lets errors flow to the API without try/catch blocks.
/// Use <see cref="Result{T}"/> when a successful call also produces a value.
/// </para>
/// <para>Working with results:</para>
/// <list type="bullet">
///   <item><description>Check <see cref="IsFailure"/> (or <see cref="IsSuccess"/>) before using <see cref="Error"/>. The property is
///   <see langword="null"/> for a successful result, and the nullable annotations on <see cref="IsFailure"/> and <see cref="IsSuccess"/>
///   tell the compiler when it is not: using it without the check is warning CS8602/CS8604, which the build treats as an error
///   (ADR-0047). Nothing throws at run time.</description></item>
///   <item><description>Propagate failures by returning the error: <c>if (result.IsFailure) return result.Error;</c>.
///   The implicit conversion from <see cref="Results.Error"/> turns it into the expected result type.</description></item>
///   <item><description>Never ignore a returned result. Analyzer APP001 reports an error when a <see cref="Result"/> is discarded;
///   use <c>_ = ...</c> only when ignoring it is really intended.</description></item>
///   <item><description>Pipeline behaviors rely on the result: the transaction behavior commits only successful commands,
///   and the logging behavior records failures with their <see cref="Error.Code"/>.</description></item>
///   <item><description>Controllers convert results with <c>this.ToActionResult(result)</c>: success becomes HTTP 204 (or 200 with a value),
///   failure becomes <c>ProblemDetails</c> with the status derived from <see cref="Error.Type"/>.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // aggregate method
/// public Result Rename(string name)
/// {
///     if (string.IsNullOrWhiteSpace(name))
///     {
///         return Error.Validation("knowledge.category.invalid_name", "Name is required.");
///     }
///
///     Name = name;
///     return Result.Success();
/// }
///
/// // command handler
/// var category = await categories.GetAsync(id, cancellationToken);
/// if (category is null)
/// {
///     return CategoryErrors.NotFound;
/// }
///
/// return category.Rename(command.Name);
/// </code>
/// </example>
/// <seealso cref="Result{T}"/>
/// <seealso cref="Results.Error"/>
public class Result : IResultFactory<Result>
{
    private readonly Error? _error;

    /// <summary>Initializes a result; <see langword="null"/> means success.</summary>
    /// <param name="error">The failure reason, or <see langword="null"/> for a successful result.</param>
    /// <remarks>Protected so that only <see cref="Result{T}"/> can derive from it; create results with the static factory methods.</remarks>
    protected Result(Error? error) => _error = error;

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    /// <remarks>When <see langword="false"/>, the compiler knows that <see cref="Error"/> is not <see langword="null"/>.</remarks>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _error is null;

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    /// <remarks>When <see langword="true"/>, the compiler knows that <see cref="Error"/> is not <see langword="null"/>.</remarks>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => _error is not null;

    /// <summary>Gets the failure reason, or <see langword="null"/> for a successful result.</summary>
    /// <remarks>
    /// Read it after checking <see cref="IsFailure"/> or <see cref="IsSuccess"/>; the compiler then treats it as not
    /// <see langword="null"/>. Reading it without the check never throws but yields a nullable value, so passing it on or using its members
    /// is a nullable warning, an error in this build (ADR-0047).
    /// </remarks>
    public Error? Error => _error;

    /// <summary>Creates a successful result without a value.</summary>
    /// <returns>A successful <see cref="Result"/>.</returns>
    public static Result Success() => new(null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The reason of the failure.</param>
    /// <returns>A failed <see cref="Result"/>. The same can be achieved by returning <paramref name="error"/> directly thanks to the implicit conversion.</returns>
    public static Result Failure(Error error) => new(error);

    /// <summary>Creates a successful result carrying a value; shorthand for <see cref="Result{T}.Success(T)"/> with type inference.</summary>
    /// <typeparam name="T">Type of the value, inferred from <paramref name="value"/>.</typeparam>
    /// <param name="value">The value produced by the operation.</param>
    /// <returns>A successful <see cref="Result{T}"/>.</returns>
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    static Result IResultFactory<Result>.FromError(Error error) => Failure(error);

    /// <summary>Converts an <see cref="Results.Error"/> into a failed <see cref="Result"/>, so that methods can <c>return SomeErrors.X;</c>.</summary>
    /// <param name="error">The reason of the failure.</param>
    /// <returns>A failed <see cref="Result"/>.</returns>
    public static implicit operator Result(Error error) => Failure(error);
}
