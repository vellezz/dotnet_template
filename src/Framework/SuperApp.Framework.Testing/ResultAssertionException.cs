namespace SuperApp.Framework.Testing;

/// <summary>Thrown by <see cref="ResultAssert"/> when a test expected a different outcome of a <c>Result</c>; fails the test.</summary>
/// <remarks>
/// Test frameworks report any exception thrown by a test as its failure, so this type needs no dependency on xUnit. The message names
/// the expected outcome and, for an unexpected failure, the error code and message, which is what a reader of a failed test needs first.
/// </remarks>
/// <param name="message">What was expected and what the result contained.</param>
public sealed class ResultAssertionException(string message) : Exception(message);
