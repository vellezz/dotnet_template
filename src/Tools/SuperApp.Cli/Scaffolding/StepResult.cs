namespace SuperApp.Cli.Scaffolding;

/// <summary>One line of the report of a scaffolding command.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Target">File or directory relative to the repository root, or the external command that ran.</param>
/// <param name="Detail">What the step did, e.g. <c>ProjectReference to Billing.Infrastructure</c>.</param>
internal sealed record StepResult(StepStatus Status, string Target, string Detail);
