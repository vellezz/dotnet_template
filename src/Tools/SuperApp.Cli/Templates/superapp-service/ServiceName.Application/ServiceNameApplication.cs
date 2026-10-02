using System.Reflection;

namespace ServiceName.Application;

/// <summary>
/// Marker of the ServiceName Application assembly, used wherever the assembly has to be scanned.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddServiceNameCore</c> passes <see cref="Assembly"/> to <c>AddAppApplication</c>, which registers from it the command handlers,
/// FluentValidation validators and domain event handlers. Tests use it to inspect the registered pipeline. Keep this class as it is;
/// it contains no logic.
/// </para>
/// <para>
/// What belongs to this assembly: one folder per use case under <c>Features/{Aggregate}/{UseCase}/</c> with the command or query, its
/// validator, DTOs and the command handler (query handlers live in Infrastructure, ADR-0026), plus ports such as <c>I{Foreign}Gateway</c>.
/// </para>
/// </remarks>
public static class ServiceNameApplication
{
    /// <summary>Gets the Application assembly of the service.</summary>
    public static Assembly Assembly => typeof(ServiceNameApplication).Assembly;
}
