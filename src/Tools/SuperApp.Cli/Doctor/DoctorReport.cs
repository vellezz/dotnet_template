namespace SuperApp.Cli.Doctor;

/// <summary>Result of one <c>doctor</c> run: the findings and what <c>--fix</c> changed.</summary>
/// <param name="Findings">Findings of all rules that ran, in rule order.</param>
/// <param name="FixedFiles">Files changed by <c>--fix</c>; empty without it.</param>
internal sealed record DoctorReport(IReadOnlyList<Finding> Findings, IReadOnlyList<string> FixedFiles)
{
    /// <summary>Number of findings with <see cref="Severity.Error"/>.</summary>
    public int Errors => Findings.Count(finding => finding.Severity == Severity.Error);

    /// <summary>Number of findings with <see cref="Severity.Warning"/>.</summary>
    public int Warnings => Findings.Count(finding => finding.Severity == Severity.Warning);
}
