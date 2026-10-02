namespace SuperApp.Cli.Scaffolding;

/// <summary>Result of running (or planning) a scaffolding command.</summary>
/// <param name="Command">The command, e.g. <c>add service Billing</c>.</param>
/// <param name="Applied">Whether the steps ran; <see langword="false"/> for <c>--dry-run</c> and for <c>remove</c> without <c>--yes</c>.</param>
/// <param name="Steps">What each step did (or would do).</param>
/// <param name="NextSteps">What is left for a person (or Copilot): code, decisions and documentation the tool does not write.</param>
internal sealed record ScaffoldReport(string Command, bool Applied, IReadOnlyList<StepResult> Steps, IReadOnlyList<string> NextSteps);
