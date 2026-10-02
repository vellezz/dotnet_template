using SuperApp.Cli.Doctor;
using SuperApp.Cli.Doctor.Rules;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Tests;

/// <summary>Event IDs lie in their component's range of the register, are unique and are listed in it.</summary>
public sealed class EventIdRuleTests
{
    private const string Register = """
        | Zakres | Komponent | Użyte |
        |---|---|---|
        | 1000–1999 | Knowledge (Api, Application, Infrastructure) | 1001–1002 |
        | 3000–3999 | `SuperApp.Gateway` | — |
        """;

    [Fact]
    public void Documented_ids_in_the_own_range_pass()
    {
        using var repository = Repository(("Log.cs", 1001), ("Other.cs", 1002));

        Assert.Empty(Check(repository));
    }

    [Fact]
    public void Id_outside_every_range_is_an_error()
    {
        using var repository = Repository(("Log.cs", 1001), ("Other.cs", 1002), ("Third.cs", 5001));

        var finding = Assert.Single(Check(repository));
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("5001", finding.Message, StringComparison.Ordinal);
        Assert.Equal(1, finding.Line);
    }

    [Fact]
    public void Id_in_the_range_of_another_component_is_an_error()
    {
        using var repository = Repository(("Log.cs", 1001), ("Other.cs", 1002), ("Third.cs", 3001));

        Assert.Contains(Check(repository), finding => finding.Severity == Severity.Error && finding.Message.Contains("another component", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_id_is_an_error()
    {
        using var repository = Repository(("Log.cs", 1001), ("Other.cs", 1001), ("Third.cs", 1002));

        Assert.Contains(Check(repository), finding => finding.Severity == Severity.Error && finding.Message.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Register_and_code_drift_are_warnings()
    {
        using var repository = Repository(("Log.cs", 1001), ("Other.cs", 1003));

        var findings = Check(repository);
        Assert.All(findings, finding => Assert.Equal(Severity.Warning, finding.Severity));
        Assert.Contains(findings, finding => finding.Message.Contains("1003 of Knowledge.Api is not listed", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.Message.Contains("1002 is listed", StringComparison.Ordinal));
    }

    private static TestRepository Repository(params (string File, int Id)[] logs)
    {
        var repository = new TestRepository().Write(EventIdRegister.Path, Register).Project("src/Services/Knowledge/Knowledge.Api/Knowledge.Api.csproj");
        foreach (var (file, id) in logs)
        {
            repository.Write($"src/Services/Knowledge/Knowledge.Api/{file}", $"[LoggerMessage({id}, LogLevel.Information, \"Message\")]");
        }

        return repository;
    }

    private static List<Finding> Check(TestRepository repository) =>
        [.. new EventIdRule().Check(repository.Scan(), new DoctorSettings([]))];
}
