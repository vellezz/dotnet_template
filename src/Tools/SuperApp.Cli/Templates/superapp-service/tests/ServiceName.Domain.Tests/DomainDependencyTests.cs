namespace ServiceName.Domain.Tests;

/// <summary>
/// Guards the dependency rule of the Domain project and is the place for the service's domain tests.
/// </summary>
/// <remarks>
/// Add one test class per aggregate or value object next to this one. Domain tests are plain unit tests without mocks: create the aggregate
/// through its factory, call its methods and assert the returned <c>Result</c>, the new state and the raised domain events.
/// </remarks>
public sealed class DomainDependencyTests
{
    /// <summary>
    /// The Domain assembly references only <c>SuperApp.Framework.Domain</c> and the base class library, so no EF Core, MediatR or other technical
    /// dependency can creep in (ADR-0002).
    /// </summary>
    [Fact]
    public void Domain_references_only_framework_domain()
    {
        var references = ServiceNameDomain.Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard");

        Assert.All(references, name => Assert.Equal("SuperApp.Framework.Domain", name));
    }
}
