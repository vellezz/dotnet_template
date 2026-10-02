using System.CommandLine;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding;

namespace SuperApp.Cli.Commands;

/// <summary>Runs a scaffolding plan for the <c>add</c> and <c>remove</c> commands and prints its report.</summary>
/// <remarks>
/// <para>
/// The plan is built from the scanned repository; building it checks every precondition, so a refused command changes nothing. With
/// <c>--dry-run</c> (and for <c>remove</c> without <c>--yes</c>) only the steps are listed. Otherwise the steps run in order and the report
/// lists what changed, what was already there and what is left for a person.
/// </para>
/// <para>Text output: one line per step (<c>changed</c>, <c>created</c>, <c>deleted</c>, <c>ran</c>, <c>unchanged</c>, <c>planned</c>) and the next steps.</para>
/// </remarks>
internal static class ScaffoldRunner
{
    /// <summary>Builds and runs (or lists) a plan.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="common">Options shared by all commands.</param>
    /// <param name="plan">Builds the plan from the scanned repository; throws <see cref="ScaffoldException"/> when a precondition fails.</param>
    /// <param name="apply">Whether to perform the steps.</param>
    /// <returns>The exit code.</returns>
    public static int Run(ParseResult parseResult, CommonOptions common, Func<RepositoryModel, ScaffoldPlan> plan, bool apply)
    {
        var output = common.Output(parseResult);
        if (common.FindRoot(parseResult, output) is not { } root)
        {
            return ExitCodes.NotFound;
        }

        try
        {
            var model = RepositoryScanner.Scan(root);
            var report = plan(model).Run(new ScaffoldContext(model.Files, new ProcessRunner(root)), apply);
            Write(output, report);
            return ExitCodes.Success;
        }
        catch (ScaffoldException exception)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { error = exception.Message });
            }

            output.Error(exception.Message);
            return ExitCodes.Failed;
        }
    }

    private static void Write(OutputWriter output, ScaffoldReport report)
    {
        if (output.IsJson)
        {
            output.WriteJson(report);
            return;
        }

        output.Line(report.Applied ? report.Command : $"{report.Command} (plan only: nothing was changed)");
        foreach (var step in report.Steps)
        {
            output.Line($"  {step.Status.ToString().ToLowerInvariant(),-9} {step.Target}  {step.Detail}");
        }

        if (!report.Applied)
        {
            output.Line();
            output.Line(report.Command.StartsWith("remove", StringComparison.Ordinal)
                ? "Run again with --yes to apply."
                : "Run again without --dry-run to apply.");
            return;
        }

        output.Line();
        output.Line("Next steps:");
        foreach (var next in report.NextSteps)
        {
            output.Line($"  - {next}");
        }
    }
}
