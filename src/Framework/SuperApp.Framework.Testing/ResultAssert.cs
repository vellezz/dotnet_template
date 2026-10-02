using SuperApp.Framework.Domain.Results;

namespace SuperApp.Framework.Testing;

/// <summary>
/// Assertions that unwrap a <see cref="Result"/> or <see cref="Result{T}"/> in tests: the value of an expected success or the error of an
/// expected failure, with a readable message when the outcome is the other one (ADR-0047).
/// </summary>
/// <remarks>
/// <para>
/// Production code reads a result only through <see cref="Result{T}.TryGetValue"/> and <see cref="Result.IsFailure"/>; there is no
/// throwing <c>Value</c> property. Tests, however, often need the value of a result that must succeed (an aggregate created as test data)
/// or the error of one that must fail. These helpers do exactly that and fail the test with <see cref="ResultAssertionException"/>,
/// whose message contains the error code, instead of a <see cref="NullReferenceException"/> or a meaningless default value.
/// </para>
/// <para>
/// Only test projects reference this assembly. Use it for arrange steps and for assertions on the outcome; compare the error itself with
/// the expected <c>{Aggregate}Errors</c> instance, because errors are records and compare by value.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var category = ResultAssert.Success(Category.Create("Sleep", "sleep"));
///
/// Assert.Equal(CategoryErrors.InvalidSlug, ResultAssert.Failure(Category.Create("Sleep", "Sleep")));
/// </code>
/// </example>
public static class ResultAssert
{
    /// <summary>Asserts that <paramref name="result"/> succeeded and returns its value.</summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="result">The result that must be a success.</param>
    /// <returns>The value carried by the successful result.</returns>
    /// <exception cref="ResultAssertionException">The result is a failure; the message contains its error code and message.</exception>
    public static T Success<T>(Result<T> result) =>
        result.TryGetValue(out var value, out var error) ? value : throw Unexpected(error);

    /// <summary>Asserts that <paramref name="result"/> succeeded.</summary>
    /// <param name="result">The result that must be a success.</param>
    /// <exception cref="ResultAssertionException">The result is a failure; the message contains its error code and message.</exception>
    public static void Success(Result result)
    {
        if (result.IsFailure)
        {
            throw Unexpected(result.Error);
        }
    }

    /// <summary>Asserts that <paramref name="result"/> failed and returns its error.</summary>
    /// <param name="result">The result that must be a failure, of any value type.</param>
    /// <returns>The error of the failed result.</returns>
    /// <exception cref="ResultAssertionException">The result is a success.</exception>
    public static Error Failure(Result result) =>
        result.IsFailure ? result.Error : throw new ResultAssertionException("Expected a failed result, but it succeeded.");

    private static ResultAssertionException Unexpected(Error error) =>
        new($"Expected a successful result, but it failed with {error.Code} ({error.Type}): {error.Message}");
}
