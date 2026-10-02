using SuperApp.Cli.Doctor;
using SuperApp.Cli.Doctor.Rules;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Tests;

/// <summary>SuperApp.sln mirrors SuperApp.slnx, every project is in the solution, and --fix regenerates the .sln.</summary>
public sealed class SolutionFilesRuleTests
{
    private const string Slnx = """
        <Solution>
          <Folder Name="/src/" />
          <Folder Name="/src/Framework/">
            <Project Path="src/Framework/A.Domain/A.Domain.csproj" />
          </Folder>
          <Folder Name="/src/Services/Knowledge/">
            <Project Path="src/Services/Knowledge/Knowledge.Api/Knowledge.Api.csproj" />
          </Folder>
        </Solution>
        """;

    [Fact]
    public void Generated_sln_matches_the_slnx()
    {
        using var repository = Repository();
        repository.Write(SolutionFiles.Sln, SolutionFiles.WriteSln(SolutionFiles.ReadSlnx(Slnx)));

        Assert.Empty(Check(repository));
        Assert.Equal(SolutionFiles.ReadSlnx(Slnx).OrderBy(entry => entry.ProjectPath, StringComparer.Ordinal), SolutionFiles.ReadSln(repository.Read(SolutionFiles.Sln)));
    }

    [Fact]
    public void Project_in_another_folder_of_the_sln_is_reported_and_fixed()
    {
        using var repository = Repository();
        var misplaced = SolutionFiles.ReadSlnx(Slnx).Select(entry => entry with { Folder = "src/Services/Knowledge" }).ToList();
        repository.Write(SolutionFiles.Sln, SolutionFiles.WriteSln(misplaced));

        var findings = Check(repository);
        Assert.Contains(findings, finding => finding.Message.Contains("A.Domain", StringComparison.Ordinal) && finding.Severity == Severity.Error);

        var changed = new SolutionFilesRule().Fix(repository.Scan());

        Assert.Equal([SolutionFiles.Sln], changed);
        Assert.Empty(Check(repository));
        Assert.Empty(new SolutionFilesRule().Fix(repository.Scan()));
    }

    [Fact]
    public void Project_missing_from_the_slnx_is_reported()
    {
        using var repository = Repository();
        repository.Write(SolutionFiles.Sln, SolutionFiles.WriteSln(SolutionFiles.ReadSlnx(Slnx)));
        repository.Project("src/Tools/New.Tool/New.Tool.csproj");

        var finding = Assert.Single(Check(repository));
        Assert.Contains("src/Tools/New.Tool/New.Tool.csproj", finding.Message, StringComparison.Ordinal);
        Assert.Contains("dotnet sln SuperApp.slnx add", finding.Fix, StringComparison.Ordinal);
    }

    private static TestRepository Repository() =>
        new TestRepository()
            .Write(SolutionFiles.Slnx, Slnx)
            .Project("src/Framework/A.Domain/A.Domain.csproj")
            .Project("src/Services/Knowledge/Knowledge.Api/Knowledge.Api.csproj");

    private static List<Finding> Check(TestRepository repository) =>
        [.. new SolutionFilesRule().Check(repository.Scan(), new DoctorSettings([]))];
}
