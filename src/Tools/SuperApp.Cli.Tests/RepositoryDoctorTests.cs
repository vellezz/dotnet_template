using SuperApp.Cli.Doctor;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Tests;

/// <summary>
/// The repository itself passes <c>dotnet superapp doctor</c> without errors, so every <c>dotnet test</c> (locally and in CI) checks its
/// consistency: solutions, registrations, ports, event IDs and documentation.
/// </summary>
public sealed class RepositoryDoctorTests
{
    [Fact]
    public void Repository_has_no_doctor_errors()
    {
        var root = RepositoryRoot.Find(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Tests must run inside the repository.");

        var report = DoctorRunner.Run(root, DoctorRules.All, fix: false);

        var errors = report.Findings.Where(finding => finding.Severity == Severity.Error)
            .Select(finding => $"{finding.Rule}: {finding.Message} [{finding.File}:{finding.Line}] fix: {finding.Fix}");
        Assert.True(report.Errors == 0, "dotnet superapp doctor reports:\n" + string.Join('\n', errors));
    }
}
