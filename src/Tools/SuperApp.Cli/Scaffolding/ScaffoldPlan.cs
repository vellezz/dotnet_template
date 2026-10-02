namespace SuperApp.Cli.Scaffolding;

/// <summary>The ordered steps of one scaffolding command (<c>add service</c>, <c>remove bff</c>, …) and what is left to do by hand.</summary>
/// <remarks>
/// Plans are built by <c>ServicePlans</c>, <c>BffPlans</c> and <c>ClientPlans</c> after all preconditions are checked, so a plan that
/// exists can run. <see cref="Run"/> without applying lists the steps as <see cref="StepStatus.Planned"/>; with applying it runs them in
/// order and stops at the first <see cref="ScaffoldException"/>, which carries the steps done so far.
/// </remarks>
/// <param name="Command">The command, e.g. <c>add service Billing</c>.</param>
/// <param name="Steps">The steps in the order they run.</param>
/// <param name="NextSteps">What is left for a person: domain code, decisions, documentation.</param>
internal sealed record ScaffoldPlan(string Command, IReadOnlyList<ScaffoldStep> Steps, IReadOnlyList<string> NextSteps)
{
    /// <summary>Runs or lists the steps.</summary>
    /// <param name="context">Files and processes of the repository.</param>
    /// <param name="apply">Whether to perform the steps; <see langword="false"/> only lists them.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ScaffoldException">A step failed; the message lists the steps already applied.</exception>
    public ScaffoldReport Run(ScaffoldContext context, bool apply)
    {
        if (!apply)
        {
            return new ScaffoldReport(Command, false, [.. Steps.Select(step => new StepResult(StepStatus.Planned, step.Target, step.Description))], NextSteps);
        }

        var results = new List<StepResult>();
        foreach (var step in Steps)
        {
            try
            {
                results.AddRange(step.Apply(context));
            }
            catch (ScaffoldException exception)
            {
                var done = results.Where(result => result.Status != StepStatus.Unchanged).Select(result => $"  {result.Status.ToString().ToLowerInvariant()} {result.Target}");
                throw new ScaffoldException($"{exception.Message}\nStopped at: {step.Description} ({step.Target}).\nAlready applied:\n{string.Join('\n', done)}");
            }
        }

        return new ScaffoldReport(Command, true, results, NextSteps);
    }
}
