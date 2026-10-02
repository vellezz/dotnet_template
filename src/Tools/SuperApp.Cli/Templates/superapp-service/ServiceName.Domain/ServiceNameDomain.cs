using System.Reflection;

namespace ServiceName.Domain;

/// <summary>
/// Marker of the ServiceName Domain assembly, used wherever the assembly has to be scanned.
/// </summary>
/// <remarks>
/// <para>
/// The write and read database contexts return <see cref="Assembly"/> as their <c>DomainAssembly</c>: every strongly typed ID and
/// single-value object found in it gets its EF Core value conversion automatically (ADR-0023, ADR-0024). Tests use it to check that
/// Domain references nothing but <c>SuperApp.Framework.Domain</c>. Keep this class as it is; it contains no logic.
/// </para>
/// <para>
/// What belongs to this assembly: aggregates, entities, value objects, strongly typed IDs, domain events, <c>{Aggregate}Errors</c> and
/// one repository interface per aggregate, grouped in folders per aggregate. No EF Core, MediatR or other technical dependencies.
/// </para>
/// </remarks>
public static class ServiceNameDomain
{
    /// <summary>Gets the Domain assembly of the service.</summary>
    public static Assembly Assembly => typeof(ServiceNameDomain).Assembly;
}
