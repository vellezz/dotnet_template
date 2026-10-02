using System.CommandLine;
using SuperApp.Cli.Doctor;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp doctor [--fix] [--rule &lt;id&gt;…]</c>: checks that the repository is consistent.</summary>
/// <remarks>
/// <para>
/// Runs the rules of <see cref="DoctorRules.All"/> (or the ones named by <c>--rule</c>) and prints every finding with its file and fix.
/// Exit code <see cref="ExitCodes.FindingsFound"/> when there is at least one error; warnings alone exit with <see cref="ExitCodes.Success"/>.
/// </para>
/// <para>
/// <c>--fix</c> first runs the mechanical fixes of the selected rules (e.g. regenerating <c>SuperApp.sln</c>), then checks again and lists
/// the changed files. Run it after adding or moving projects, and before handing over a change (checklist of the developer guide).
/// </para>
/// </remarks>
internal static class DoctorCommand
{
    /// <summary>Creates the command.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>doctor</c> command.</returns>
    public static Command Create(CommonOptions common)
    {
        var fix = new Option<bool>("--fix") { Description = "Fix mechanically fixable findings first (e.g. regenerate SuperApp.sln), then check." };
        var rule = new Option<string[]>("--rule")
        {
            Description = $"Run only these rules: {string.Join(", ", DoctorRules.All.Select(r => r.Id))}.",
            AllowMultipleArgumentsPerToken = true,
        };
        rule.AcceptOnlyFromAmong([.. DoctorRules.All.Select(r => r.Id)]);

        var command = new Command("doctor", "Check that the repository is consistent: solutions, registrations of services and BFFs, ports, event IDs, documentation.")
        {
            fix,
            rule,
        };

        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var selected = parseResult.GetValue(rule) is { Length: > 0 } ids
                ? [.. DoctorRules.All.Where(r => ids.Contains(r.Id, StringComparer.Ordinal))]
                : DoctorRules.All;
            var report = DoctorRunner.Run(root, selected, parseResult.GetValue(fix));
            Write(output, report, selected);
            return report.Errors > 0 ? ExitCodes.FindingsFound : ExitCodes.Success;
        });

        return command;
    }

    private static void Write(Output.OutputWriter output, DoctorReport report, IReadOnlyList<IDoctorRule> rules)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                errors = report.Errors,
                warnings = report.Warnings,
                rules = rules.Select(rule => rule.Id),
                fixedFiles = report.FixedFiles,
                findings = report.Findings,
            });
            return;
        }

        foreach (var file in report.FixedFiles)
        {
            output.Line($"fixed  {file}");
        }

        foreach (var rule in rules)
        {
            var findings = report.Findings.Where(finding => finding.Rule == rule.Id).ToList();
            output.Line($"{(findings.Any(finding => finding.Severity == Severity.Error) ? "FAIL" : findings.Count > 0 ? "WARN" : "ok  ")}  {rule.Id}  {rule.Description}");
            foreach (var finding in findings)
            {
                var place = finding.File is null ? string.Empty : $" [{finding.File}{(finding.Line is { } line ? $":{line}" : string.Empty)}]";
                output.Line($"      {(finding.Severity == Severity.Error ? "error" : "warning")}: {finding.Message}{place}");
                if (finding.Fix is not null)
                {
                    output.Line($"        fix: {finding.Fix}");
                }
            }
        }

        output.Line();
        output.Line($"{report.Errors} error(s), {report.Warnings} warning(s).");
    }
}
