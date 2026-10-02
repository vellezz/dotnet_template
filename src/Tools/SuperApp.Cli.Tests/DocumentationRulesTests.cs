using SuperApp.Cli.Doctor;
using SuperApp.Cli.Doctor.Rules;

namespace SuperApp.Cli.Tests;

/// <summary>Links, anchors and quoted repository paths in the documentation resolve; configured exceptions are skipped.</summary>
public sealed class DocumentationRulesTests
{
    [Fact]
    public void Link_to_an_existing_heading_with_polish_letters_passes()
    {
        using var repository = new TestRepository()
            .Write("docs/a.md", "# Rozdział\n\n## 2.1 Zdarzenia z backendu: `SuperApp.AnalyticsForwarder`\n")
            .Write("docs/b.md", "[x](a.md#21-zdarzenia-z-backendu-superappanalyticsforwarder) [y](a.md) [z](https://example.com/none)\n");

        Assert.Empty(new DocumentationLinksRule().Check(repository.Scan(), new DoctorSettings([])));
    }

    [Fact]
    public void Missing_file_and_missing_anchor_are_reported()
    {
        using var repository = new TestRepository()
            .Write("docs/a.md", "# Title\n")
            .Write("docs/b.md", "[x](a.md#other)\n\n[y](missing.md)\n");

        var findings = new DocumentationLinksRule().Check(repository.Scan(), new DoctorSettings([])).ToList();

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, finding => finding.Message.Contains("heading", StringComparison.Ordinal) && finding.Line == 1);
        Assert.Contains(findings, finding => finding.Message.Contains("docs/missing.md", StringComparison.Ordinal) && finding.Line == 3);
    }

    [Fact]
    public void Quoted_paths_must_exist_except_placeholders_and_exceptions()
    {
        using var repository = new TestRepository()
            .Write("src/Real/File.cs", "class A;")
            .Write("docs/a.md", "`src/Real/File.cs:12` `src/{Service}/x` `src/Billing/Billing.Api` `src/Gone.cs`\n");

        var finding = Assert.Single(new DocumentationPathsRule().Check(repository.Scan(), new DoctorSettings(["Billing"])));
        Assert.Contains("src/Gone.cs", finding.Message, StringComparison.Ordinal);
    }
}
