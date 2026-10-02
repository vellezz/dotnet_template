namespace SuperApp.Cli.Scaffolding;

/// <summary>
/// A scaffolding command cannot continue: an invalid name, a precondition not met (e.g. a BFF still uses the service being removed) or a
/// failed external step (<c>dotnet new</c>, <c>dotnet ef</c>, <c>refitter</c>).
/// </summary>
/// <remarks>
/// The message is shown to the user as it is, so it says what is wrong and what to do. Preconditions are checked before the first change;
/// a failure of an external step stops the plan, and the steps already applied are listed, so the user can finish or revert them.
/// </remarks>
/// <param name="message">What is wrong and how to fix it.</param>
internal sealed class ScaffoldException(string message) : Exception(message);
