namespace SleepDiary.Domain.Tests;

public sealed class DomainDependencyTests
{
    /// <summary>Domain depends only on SuperApp.Framework.Domain and the standard library (ADR-0002).</summary>
    [Fact]
    public void Domain_references_only_framework_domain()
    {
        var references = SleepDiaryDomain.Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard");

        Assert.All(references, name => Assert.Equal("SuperApp.Framework.Domain", name));
    }
}
